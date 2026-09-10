using System;
using System.IO;
using System.Threading.Tasks;
using DE.Share.Data;
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
            Debug.Log("Data framework validation passed: 2 SpaceData rows; GetRow(FromInt32(1001)) = MainCity, 200. Synchronous and caller-scheduled prewarm, static cache and reinitialization verified.");
        }

        private static SpaceDataTable LoadSample(string rootDirectory, bool useWorker)
        {
            DataRuntime.Initialize(new ExcelDataProvider());
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
    }
}
