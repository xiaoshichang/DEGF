using System;
using System.Collections.Generic;
using DE.Share.Data.DataDescribe;
using ExcelDataReader;

namespace DE.Share.Data.DataProvider
{
    internal sealed class ExcelDataTableReader : IDataTableReader
    {
        private readonly IExcelDataReader reader;
        private readonly DataTableDescribe describe;
        private readonly int[] columnMap;
        private readonly string[] headers;
        private readonly bool[] boundColumns;
        private bool disposed;
        private bool hasCurrentRow;

        public ExcelDataTableReader(IExcelDataReader reader, string sourcePath, DataTableDescribe describe)
        {
            this.reader = reader;
            this.describe = describe;
            SourcePath = sourcePath;
            SheetName = describe.SheetName;
            while (!string.Equals(reader.Name, SheetName, StringComparison.Ordinal))
            {
                if (!reader.NextResult())
                {
                    throw Error("The specified worksheet does not exist.");
                }
            }
            if (reader.MergeCells != null && reader.MergeCells.Length > 0)
            {
                throw Error("Merged cells are not supported in data worksheets.");
            }
            if (!reader.Read())
            {
                throw Error("The worksheet has no header row.");
            }
            RowNumber = reader.Depth + 1L;
            headers = new string[reader.FieldCount];
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < headers.Length; index++)
            {
                CheckCellError(index, null);
                object value = reader.GetValue(index);
                if (value == null || value is string empty && empty.Length == 0)
                {
                    continue;
                }
                if (!(value is string name) || string.IsNullOrWhiteSpace(name))
                {
                    throw Error($"Column {index + 1} must have a non-blank text header.");
                }
                if (indices.ContainsKey(name))
                {
                    throw Error("Duplicate column header.", name);
                }
                indices.Add(name, index);
                headers[index] = name;
            }
            columnMap = new int[describe.Columns.Count];
            boundColumns = new bool[headers.Length];
            for (int index = 0; index < columnMap.Length; index++)
            {
                string name = describe.Columns[index].ColumnName;
                if (!indices.TryGetValue(name, out columnMap[index]))
                {
                    throw Error("Required column is missing.", name);
                }
                boundColumns[columnMap[index]] = true;
            }
        }

        public string SourcePath { get; }
        public string SheetName { get; }
        public long RowNumber { get; private set; }

        public bool Read()
        {
            ThrowIfDisposed();
            hasCurrentRow = false;
            try
            {
                while (reader.Read())
                {
                    RowNumber = reader.Depth + 1L;
                    bool nonempty = false;
                    for (int index = 0; index < reader.FieldCount; index++)
                    {
                        string header = index < headers.Length ? headers[index] : null;
                        if (reader.GetCellError(index).HasValue)
                        {
                            if (header == null || boundColumns[index])
                            {
                                CheckCellError(index, header);
                            }
                            nonempty = true;
                            continue;
                        }
                        object value = reader.GetValue(index);
                        if (value == null || value is string text && text.Length == 0)
                        {
                            continue;
                        }
                        nonempty = true;
                        if (header == null)
                        {
                            throw Error($"Column {index + 1} contains data but has no header.");
                        }
                    }
                    if (nonempty)
                    {
                        hasCurrentRow = true;
                        return true;
                    }
                }
                return false;
            }
            catch (Exception exception) when (!(exception is DataLoadException))
            {
                throw Error("Could not read the worksheet row.", innerException: exception);
            }
        }

        public bool IsNull(int columnIndex)
        {
            object value = GetValue(columnIndex);
            return value == null || value is string text && text.Length == 0;
        }

        public int GetInt32(int columnIndex)
        {
            return ConvertValue(columnIndex, "Int32", value => (int)ExcelValueConverter.GetInteger(value, typeof(int)));
        }

        public uint GetUInt32(int columnIndex)
        {
            return ConvertValue(columnIndex, "UInt32", value => (uint)ExcelValueConverter.GetInteger(value, typeof(uint)));
        }

        public long GetInt64(int columnIndex)
        {
            return ConvertValue(columnIndex, "Int64", value => (long)ExcelValueConverter.GetInteger(value, typeof(long)));
        }

        public ulong GetUInt64(int columnIndex)
        {
            return ConvertValue(columnIndex, "UInt64", value => (ulong)ExcelValueConverter.GetInteger(value, typeof(ulong)));
        }

        public float GetSingle(int columnIndex)
        {
            return ConvertValue(columnIndex, "Single", ExcelValueConverter.GetSingle);
        }

        public double GetDouble(int columnIndex)
        {
            return ConvertValue(columnIndex, "Double", ExcelValueConverter.GetDouble);
        }

        public bool GetBoolean(int columnIndex)
        {
            return ConvertValue(columnIndex, "Boolean", ExcelValueConverter.GetBoolean);
        }

        public string GetString(int columnIndex)
        {
            return ConvertValue(columnIndex, "String", ExcelValueConverter.GetString);
        }

        public TEnum GetEnum<TEnum>(int columnIndex) where TEnum : struct, Enum
        {
            return ConvertValue(columnIndex, typeof(TEnum).FullName, ExcelValueConverter.GetEnum<TEnum>);
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                reader.Dispose();
            }
        }

        private T ConvertValue<T>(int index, string targetType, Func<object, T> convert)
        {
            object value = GetValue(index);
            try
            {
                return convert(value);
            }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException || exception is ArgumentException || exception is InvalidCastException)
            {
                throw Error($"Cannot convert the cell to {targetType}: {exception.Message}", describe.Columns[index].ColumnName, exception);
            }
        }

        private object GetValue(int index)
        {
            ThrowIfDisposed();
            if (!hasCurrentRow)
            {
                throw new InvalidOperationException("Call Read successfully before reading a cell.");
            }
            if (index < 0 || index >= columnMap.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            int physicalIndex = columnMap[index];
            CheckCellError(physicalIndex, describe.Columns[index].ColumnName);
            return reader.GetValue(physicalIndex);
        }

        private void CheckCellError(int index, string column)
        {
            var error = reader.GetCellError(index);
            if (error.HasValue)
            {
                throw Error($"The cell contains Excel error '{error.Value}'.", column);
            }
        }

        private DataLoadException Error(string message, string columnName = null, Exception innerException = null)
        {
            return new DataLoadException(message, SourcePath, SheetName, RowNumber, columnName, innerException);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ExcelDataTableReader));
            }
        }
    }
}
