using System;
using System.Collections.Generic;
using DE.Share.Data.DataDescribe;
using DE.Share.Data.DataProvider;

namespace DE.Share.Data
{
    public abstract class DataTable<TRow> : IDataTable where TRow : DataRow
    {
        private readonly Dictionary<DataTableKey, TRow> rows;

        protected DataTable(DataTableDescribe describe, Dictionary<DataTableKey, TRow> rows)
        {
            Describe = describe ?? throw new ArgumentNullException(nameof(describe));
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }
            foreach (var pair in rows)
            {
                if (pair.Value == null || pair.Key != pair.Value.Id)
                {
                    throw new ArgumentException("Every table key must match a non-null row's Id.", nameof(rows));
                }
            }
            this.rows = new Dictionary<DataTableKey, TRow>(rows);
        }

        public DataTableDescribe Describe { get; }
        public int Count => rows.Count;

        protected static void InitializeFactory<TTable>(DataTableDescribe describe, Func<IDataProvider, string, TTable> loader)
            where TTable : DataTable<TRow>
        {
            DataTableFactory<TTable>.Initialize(describe, loader);
        }

        public TRow GetRow(DataTableKey key)
        {
            if (!rows.TryGetValue(key, out var row))
            {
                throw new KeyNotFoundException($"Table '{Describe.TableName}' does not contain key '{key}'.");
            }
            return row;
        }
    }
}
