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
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (describe == null)
            {
                throw new ArgumentNullException(nameof(describe));
            }
            if (materialize == null)
            {
                throw new ArgumentNullException(nameof(materialize));
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
                        var row = materialize(reader);
                        if (row == null)
                        {
                            throw new InvalidOperationException("The row factory returned null.");
                        }
                        if (positions.TryGetValue(row.Id, out long previousRow))
                        {
                            throw new DataLoadException($"Duplicate key '{row.Id}' in table '{describe.TableName}'; first seen at row {previousRow}.",
                                reader.SourcePath, reader.SheetName, reader.RowNumber, describe.Columns[0].ColumnName);
                        }
                        rows.Add(row.Id, row);
                        positions.Add(row.Id, reader.RowNumber);
                    }
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
