using System;
using System.IO;
using System.Threading.Tasks;
using DE.Share.Data;
using DE.Share.Data.DataDescribe;
using DE.Share.Data.DataProvider;
using UnityEditor;
using UnityEngine;

namespace Demo.Data.Editor
{
    public static class DataFrameworkValidation
    {
        [MenuItem("DEGF/Validate Data Tables")]
        public static void Validate()
        {
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string rootDirectory = Path.Combine(repositoryRoot, "Data", "Excel");
            var first = LoadSample(rootDirectory, false);
            var second = LoadSample(rootDirectory, true);
            if (ReferenceEquals(first, second))
            {
                throw new InvalidOperationException("Shutdown did not clear the cached table.");
            }
            if (first.GetRow(DataTableKey.FromInt32(1001)).Name != "MainCity")
            {
                throw new InvalidOperationException("Shutdown invalidated an existing table snapshot.");
            }
            ValidateReaderReuse(rootDirectory);
            Debug.Log("Data framework validation passed: 2 SpaceData rows; GetRow(FromInt32(1001)) = MainCity, 200. Prewarm, cache, reinitialization, table-owned provider and Row/Lru reader reuse verified.");
        }

        private static SpaceDataTable LoadSample(string rootDirectory, bool useWorker)
        {
            DataRuntime.Initialize(() => new ExcelDataProvider());
            try
            {
                DataRuntime.SetRootDirectory(rootDirectory);
                // Exclusive initialization: finish the worker before accessing tables or shutting down.
                var table = useWorker
                    ? Task.Run(() => DataRuntime.Prewarm<SpaceDataTable>()).GetAwaiter().GetResult()
                    : DataRuntime.Prewarm<SpaceDataTable>();
                var row = table.GetRow(DataTableKey.FromInt32(1001));
                if (table.Count != 2 || row.Name != "MainCity" || row.MaxPlayers != 200)
                {
                    throw new InvalidOperationException("SpaceData.xlsx did not match the sample data.");
                }
                if (!ReferenceEquals(table, DataRuntime.GetTable<SpaceDataTable>()))
                {
                    throw new InvalidOperationException("DataRuntime did not reuse the cached table.");
                }
                if (!ReferenceEquals(table, DataRuntime.Prewarm<SpaceDataTable>()))
                {
                    throw new InvalidOperationException("Prewarm did not reuse the cached table.");
                }
                return table;
            }
            finally
            {
                DataRuntime.Shutdown();
            }
        }

        private static void ValidateReaderReuse(string rootDirectory)
        {
            CountingProvider provider = null;
            DataRuntime.Initialize(() => provider = new CountingProvider());
            ReaderReuseDataTable table;
            ValidationRow reloaded;
            try
            {
                DataRuntime.SetRootDirectory(rootDirectory);
                table = DataRuntime.GetTable<ReaderReuseDataTable>();
                if (provider.OpenCount != 0 || table.Count != 2)
                {
                    throw new InvalidOperationException("Row table creation or lazy Count performed unexpected IO.");
                }
                var first = table.GetRow(DataTableKey.FromInt32(1001));
                var second = table.GetRow(DataTableKey.FromInt32(1002));
                reloaded = table.GetRow(DataTableKey.FromInt32(1001));
                if (first.Name != "MainCity" || second.Name != "Arena" || reloaded.MaxPlayers != 200
                    || ReferenceEquals(first, reloaded) || provider.OpenCount != 1 || provider.DisposeCount != 0)
                {
                    throw new InvalidOperationException("Row/Lru did not reuse its reader across Count and cache misses.");
                }
            }
            finally
            {
                DataRuntime.Shutdown();
            }
            if (provider.DisposeCount != 1 || !ReferenceEquals(reloaded, table.GetRow(DataTableKey.FromInt32(1001))))
            {
                throw new InvalidOperationException("Shutdown did not release the provider while preserving cached rows.");
            }
            using (File.Open(Path.Combine(rootDirectory, "SpaceData.xlsx"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // An exclusive open verifies that Shutdown released the table's file handle.
            }
        }

        private sealed class CountingProvider : IDataProvider
        {
            private readonly ExcelDataProvider _Provider = new ExcelDataProvider();
            public int OpenCount { get; private set; }
            public int DisposeCount { get; private set; }

            public IDataTableReader OpenTable(string rootDirectory, DataTableDescribe describe)
            {
                OpenCount++;
                return _Provider.OpenTable(rootDirectory, describe);
            }

            public void Dispose()
            {
                DisposeCount++;
                _Provider.Dispose();
            }
        }

        private sealed class ValidationRow : DataRow
        {
            public int MaxPlayers { get; private set; }
            public string Name { get; private set; }

            public static ValidationRow Create(IDataTableReader reader)
            {
                return new ValidationRow
                {
                    Id = DataTableKey.FromInt32(reader.GetInt32(0)),
                    MaxPlayers = reader.GetInt32(1),
                    Name = reader.GetString(2)
                };
            }
        }

        private sealed class ReaderReuseDataTable : DataTable<ValidationRow>
        {
            private static readonly DataTableDescribe TableDescribe = new DataTableDescribe(
                "ReaderReuseData", "SpaceData", "SpaceData", nameof(ValidationRow), DataLoadPolicy.Row, DataCachePolicy.Lru, 1,
                new DataColumnDescribe("Id", "Id", DataValueKind.Int32, isKey: true),
                new DataColumnDescribe("MaxPlayers", "MaxPlayers", DataValueKind.Int32),
                new DataColumnDescribe("Name", "Name", DataValueKind.String));

            static ReaderReuseDataTable()
            {
                InitializeFactory(TableDescribe, (factory, root) => new ReaderReuseDataTable(factory, root));
            }

            private ReaderReuseDataTable(Func<IDataProvider> factory, string root)
                : base(TableDescribe, factory, root, ValidationRow.Create)
            {
            }
        }
    }
}
