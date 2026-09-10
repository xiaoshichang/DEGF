using System;
using System.Collections.Generic;
using DE.Share.Data.DataDescribe;
using DE.Share.Data.DataProvider;

namespace DE.Share.Data
{
    public static class DataTableLoader
    {
        public static Dictionary<DataTableKey, TRow> LoadRows<TRow>(IDataProvider provider, string rootDirectory,
            DataTableDescribe describe, Func<IDataTableReader, TRow> materialize) where TRow : DataRow
        {
            return LoadRows(provider, rootDirectory, describe, materialize, null, out _);
        }

        internal static Dictionary<DataTableKey, TRow> LoadRows<TRow>(IDataProvider provider, string rootDirectory,
            DataTableDescribe describe, Func<IDataTableReader, TRow> materialize,
            Func<DataTableKey, bool> includeRow, out int count) where TRow : DataRow
        {
            if (materialize == null)
            {
                throw new ArgumentNullException(nameof(materialize));
            }
            return ScanRows(provider, rootDirectory, describe, materialize, includeRow, out count);
        }

        internal static int CountRows(IDataProvider provider, string rootDirectory, DataTableDescribe describe)
        {
            ScanRows<DataRow>(provider, rootDirectory, describe, null, null, out int count);
            return count;
        }

        private static Dictionary<DataTableKey, TRow> ScanRows<TRow>(IDataProvider provider, string rootDirectory,
            DataTableDescribe describe, Func<IDataTableReader, TRow> materialize,
            Func<DataTableKey, bool> includeRow, out int count) where TRow : DataRow
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (describe == null)
            {
                throw new ArgumentNullException(nameof(describe));
            }
            using (var reader = provider.OpenTable(rootDirectory, describe))
            {
                if (reader == null)
                {
                    throw new InvalidOperationException("The data provider returned a null reader.");
                }
                var rows = new Dictionary<DataTableKey, TRow>();
                var positions = new Dictionary<DataTableKey, long>();
                try
                {
                    while (reader.Read())
                    {
                        var key = DataTableKey.FromInt32(reader.GetInt32(0));
                        if (positions.TryGetValue(key, out long previousRow))
                        {
                            throw new DataLoadException($"Duplicate key '{key}' in table '{describe.TableName}'; first seen at row {previousRow}.",
                                reader.SourcePath, reader.SheetName, reader.RowNumber, describe.Columns[0].ColumnName);
                        }
                        positions.Add(key, reader.RowNumber);
                        if (materialize != null && (includeRow == null || includeRow(key)))
                        {
                            var row = materialize(reader);
                            if (row == null || row.Id != key)
                            {
                                throw new InvalidOperationException("The row factory must return a non-null row with the source key.");
                            }
                            rows.Add(key, row);
                        }
                    }
                    count = positions.Count;
                    return rows;
                }
                catch (Exception error) when (!(error is DataLoadException))
                {
                    throw new DataLoadException($"Could not load table '{describe.TableName}': {error.Message}",
                        reader.SourcePath, reader.SheetName, reader.RowNumber, innerException: error);
                }
            }
        }
    }
}
