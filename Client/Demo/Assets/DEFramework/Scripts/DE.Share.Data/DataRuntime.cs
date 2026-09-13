using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using DE.Share.Data.DataProvider;

namespace DE.Share.Data
{
    /// <summary>
    /// Shares data configuration and cached tables across the process. Serialize all access;
    /// caller-scheduled prewarm must finish before any other data operation.
    /// </summary>
    public static class DataRuntime
    {
        private static readonly Dictionary<Type, IDataTable> _Tables = new();
        private static readonly Dictionary<string, Type> _TableNames = new(StringComparer.Ordinal);
        private static readonly Dictionary<Type, Func<IDataTable>> _GeneratedTableLoaders = new();
        private static Func<IDataProvider> _ProviderFactory;
        private static string _RootDirectory;

        /// <summary>The factory must return a new independently owned provider for each table.</summary>
        public static void Initialize(Func<IDataProvider> providerFactory)
        {
            if (_ProviderFactory != null)
            {
                throw new InvalidOperationException("DataRuntime is already initialized. Call Shutdown before initializing it again.");
            }
            _ProviderFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        }

        public static void SetRootDirectory(string rootDirectory)
        {
            ThrowIfNotInitialized();
            if (_RootDirectory != null)
            {
                throw new InvalidOperationException("The data root can only be set once. Call Shutdown and initialize DataRuntime again to change it.");
            }
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("A data root directory is required.", nameof(rootDirectory));
            }
            string fullPath = Path.GetFullPath(rootDirectory);
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException($"Data root '{fullPath}' does not exist.");
            }
            _RootDirectory = fullPath;
        }

        public static TTable GetTable<TTable>() where TTable : class, IDataTable
        {
            ThrowIfNotInitialized();
            var type = typeof(TTable);
            if (_RootDirectory == null)
            {
                throw new InvalidOperationException("Call SetRootDirectory before loading a table.");
            }
            if (_Tables.TryGetValue(type, out var cached))
            {
                return (TTable)cached;
            }

            var describe = DataTableFactory<TTable>.Describe;
            if (_TableNames.TryGetValue(describe.TableName, out var existingType))
            {
                throw new InvalidOperationException($"Table name '{describe.TableName}' is already used by '{existingType.FullName}' and cannot be used by '{type.FullName}'.");
            }
            _TableNames.Add(describe.TableName, type);
            var table = DataTableFactory<TTable>.Load(_ProviderFactory, _RootDirectory);
            if (table == null || !ReferenceEquals(table.Describe, describe))
            {
                throw new InvalidOperationException("The table loader must return a table with its factory description.");
            }
            _Tables.Add(type, table);
            return table;
        }

        /// <summary>
        /// Loads a Full + KeepAlive table now. Call directly or schedule it in an exclusive
        /// initialization phase and await completion before any other data operations.
        /// </summary>
        public static TTable Prewarm<TTable>() where TTable : class, IDataTable
        {
            ThrowIfNotInitialized();
            var describe = DataTableFactory<TTable>.Describe;
            if (describe.Load != DataLoadPolicy.Full || describe.Cache != DataCachePolicy.KeepAlive)
            {
                throw new InvalidOperationException($"Only Full + KeepAlive tables support prewarming; table '{describe.TableName}' uses {describe.Load} + {describe.Cache}.");
            }
            return GetTable<TTable>();
        }

        /// <summary>
        /// Registers a generated data table loader. This method is reserved for DataTable source-generator output.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void RegisterGeneratedTable<TTable>() where TTable : class, IDataTable
        {
            var type = typeof(TTable);
            if (_GeneratedTableLoaders.ContainsKey(type))
            {
                return;
            }

            _GeneratedTableLoaders.Add(type, GetTable<TTable>);
        }

        /// <summary>
        /// Creates missing table handles from the source-generated table catalog and prints statistics
        /// for every registered table.
        /// </summary>
        /// <remarks>
        /// Each detail line contains the logical table name, table type, total source row count and current
        /// cached row count. Creating a table handle applies that table's configured load policy, so a Full table
        /// may materialize rows during this call. Reading a total row count scans the source when necessary, while
        /// a Row table that has not otherwise been queried retains zero materialized rows in its cache.
        /// </remarks>
        /// <returns>The summary followed by one line per table ordered by logical name.</returns>
        public static string PrintTableStatistics()
        {
            ThrowIfNotInitialized();
            LoadAllTableHandles();
            var tables = new List<KeyValuePair<Type, IDataTable>>(_Tables);
            tables.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.Value.Describe.TableName, right.Value.Describe.TableName));

            var details = new List<string>(tables.Count);
            long totalRowCount = 0;
            long totalCachedRowCount = 0;
            foreach (var pair in tables)
            {
                var table = pair.Value;
                var rowCount = table.Count;
                var cachedRowCount = table.CachedRowCount;
                totalRowCount += rowCount;
                totalCachedRowCount += cachedRowCount;
                details.Add(
                    $"tableName={table.Describe.TableName}, tableType={pair.Key.FullName}, rowCount={rowCount}, cachedRowCount={cachedRowCount}."
                );
            }

            details.Insert(
                0,
                $"DataRuntime table statistics: tableCount={tables.Count}, rowCount={totalRowCount}, cachedRowCount={totalCachedRowCount}."
            );
            return string.Join(Environment.NewLine, details);
        }

        private static void LoadAllTableHandles()
        {
            var loaders = new List<Func<IDataTable>>(_GeneratedTableLoaders.Values);
            foreach (var load in loaders)
            {
                load();
            }
        }

        public static void Shutdown()
        {
            _Tables.Clear();
            _TableNames.Clear();
            _ProviderFactory = null;
            _RootDirectory = null;
        }

        private static void ThrowIfNotInitialized()
        {
            if (_ProviderFactory == null)
            {
                throw new InvalidOperationException("Call DataRuntime.Initialize before using data tables.");
            }
        }

    }
}
