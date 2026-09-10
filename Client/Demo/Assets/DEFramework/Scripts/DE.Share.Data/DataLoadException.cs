using System;

namespace DE.Share.Data
{
    public sealed class DataLoadException : Exception
    {
        public DataLoadException(string message, string sourcePath, string sheetName,
            long rowNumber = 0, string columnName = null, Exception innerException = null)
            : base($"{message} [Source: '{sourcePath}', Sheet: '{sheetName}', Row: {rowNumber}, Column: '{columnName ?? string.Empty}']", innerException)
        {
            SourcePath = sourcePath;
            SheetName = sheetName;
            RowNumber = rowNumber;
            ColumnName = columnName;
        }

        public string SourcePath { get; }
        public string SheetName { get; }
        public long RowNumber { get; }
        public string ColumnName { get; }
    }
}
