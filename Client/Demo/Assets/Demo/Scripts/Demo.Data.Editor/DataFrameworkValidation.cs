using System;
using System.IO;
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
            var first = LoadSample(rootDirectory);
            var second = LoadSample(rootDirectory);
            if (ReferenceEquals(first, second))
            {
                throw new InvalidOperationException("Shutdown did not clear the cached table.");
            }
            if (first.GetRow(DataTableKey.FromInt32(1001)).Name != "MainCity")
            {
                throw new InvalidOperationException("Shutdown invalidated an existing table snapshot.");
            }
            Debug.Log("Data framework validation passed: 2 SpaceData rows; GetRow(FromInt32(1001)) = MainCity, 200. Static cache and reinitialization verified.");
        }

        private static SpaceDataTable LoadSample(string rootDirectory)
        {
            DataRuntime.Initialize(new ExcelDataProvider());
            try
            {
                DataRuntime.SetRootDirectory(rootDirectory);
                var table = DataRuntime.GetTable<SpaceDataTable>();
                var row = table.GetRow(DataTableKey.FromInt32(1001));
                if (table.Count != 2 || row.Name != "MainCity" || row.MaxPlayers != 200)
                {
                    throw new InvalidOperationException("SpaceData.xlsx did not match the sample data.");
                }
                if (!ReferenceEquals(table, DataRuntime.GetTable<SpaceDataTable>()))
                {
                    throw new InvalidOperationException("DataRuntime did not reuse the cached table.");
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
