using System;
using System.Collections.Generic;

namespace DE.Share.Data.DataDescribe
{
    public sealed class DataTableDescribe
    {
        public DataTableDescribe(string tableName, string sourceName, string sheetName, string rowTypeName,
            params DataColumnDescribe[] columns)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentException("A table name is required.", nameof(tableName));
            }
            ValidateSourceName(sourceName);
            if (string.IsNullOrWhiteSpace(sheetName))
            {
                throw new ArgumentException("A worksheet name is required.", nameof(sheetName));
            }
            if (string.IsNullOrWhiteSpace(rowTypeName))
            {
                throw new ArgumentException("A row type name is required.", nameof(rowTypeName));
            }
            if (columns == null || columns.Length == 0 || columns[0] == null || !columns[0].IsKey)
            {
                throw new ArgumentException("The first described column must be the Id key.", nameof(columns));
            }
            var columnNames = new HashSet<string>(StringComparer.Ordinal);
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < columns.Length; index++)
            {
                var column = columns[index];
                if (column == null || (index > 0 && column.IsKey) || !columnNames.Add(column.ColumnName) || !propertyNames.Add(column.PropertyName))
                {
                    throw new ArgumentException("Columns must be non-null with unique names and exactly one key.", nameof(columns));
                }
            }
            TableName = tableName;
            SourceName = sourceName.Replace('\\', '/');
            SheetName = sheetName;
            RowTypeName = rowTypeName;
            Columns = Array.AsReadOnly((DataColumnDescribe[])columns.Clone());
        }

        public string TableName { get; }
        public string SourceName { get; }
        public string SheetName { get; }
        public string RowTypeName { get; }
        public IReadOnlyList<DataColumnDescribe> Columns { get; }

        private static void ValidateSourceName(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName) || sourceName.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|', '\0' }) >= 0)
            {
                throw new ArgumentException("The source must be a relative resource name without an extension.", nameof(sourceName));
            }
            var segments = sourceName.Replace('\\', '/').Split('/');
            foreach (string segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".." || segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal))
                {
                    throw new ArgumentException("The source must contain only relative directory and resource names.", nameof(sourceName));
                }
            }
            if (segments[segments.Length - 1].Contains("."))
            {
                throw new ArgumentException("The resource name must not have a file extension.", nameof(sourceName));
            }
        }
    }
}
