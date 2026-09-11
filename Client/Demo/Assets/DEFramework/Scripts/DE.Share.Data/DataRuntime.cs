using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using DE.Share.Data.DataProvider;

namespace DE.Share.Data
{
    /// <summary>
    /// Shares data configuration and cached tables across the process. Serialize all access;
    /// caller-scheduled prewarm must finish before any other data operation.
    /// </summary>
    public static class DataRuntime
    {
        private static readonly Dictionary<Type, IDataTable> _Tables = new Dictionary<Type, IDataTable>();
        private static readonly Dictionary<Type, ExceptionDispatchInfo> _LoadFailures = new Dictionary<Type, ExceptionDispatchInfo>();
        private static readonly Dictionary<string, Type> _TableNames = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly HashSet<Type> _Loading = new HashSet<Type>();
        private static Func<IDataProvider> _ProviderFactory;
        private static string _RootDirectory;
        private static bool _ShuttingDown;

        /// <summary>The factory must return a new independently owned provider for each table.</summary>
        public static void Initialize(Func<IDataProvider> providerFactory)
        {
            if (providerFactory == null)
            {
                throw new ArgumentNullException(nameof(providerFactory));
            }
            if (_ProviderFactory != null)
            {
                throw new InvalidOperationException("DataRuntime is already initialized. Call Shutdown before initializing it again.");
            }
            _ProviderFactory = providerFactory;
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
                (cached as IDataTableState)?.ThrowIfUnavailable();
                return (TTable)cached;
            }
            if (_LoadFailures.TryGetValue(type, out var failure))
            {
                failure.Throw();
            }
            if (!_Loading.Add(type))
            {
                throw new InvalidOperationException($"Recursive loading of table type '{type.FullName}' is not supported.");
            }
            try
            {
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
                return (TTable)table;
            }
            catch (Exception error)
            {
                _LoadFailures.Add(type, ExceptionDispatchInfo.Capture(error));
                throw;
            }
            finally
            {
                _Loading.Remove(type);
            }
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

        public static void Shutdown()
        {
            if (_ShuttingDown)
            {
                throw new InvalidOperationException("DataRuntime is already shutting down.");
            }
            if (_Loading.Count != 0)
            {
                throw new InvalidOperationException("DataRuntime cannot shut down while a table is loading.");
            }
            foreach (var table in _Tables.Values)
            {
                if (table is IDataTableState state && state.IsLoading)
                {
                    throw new InvalidOperationException("DataRuntime cannot shut down while a table is loading.");
                }
            }
            var errors = new List<Exception>();
            _ShuttingDown = true;
            try
            {
                foreach (var table in _Tables.Values)
                {
                    try
                    {
                        (table as IDataTableState)?.Detach();
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }
                }
            }
            finally
            {
                _Tables.Clear();
                _LoadFailures.Clear();
                _TableNames.Clear();
                _ProviderFactory = null;
                _RootDirectory = null;
                _ShuttingDown = false;
            }
            if (errors.Count != 0)
            {
                throw new AggregateException("One or more data providers could not be disposed.", errors);
            }
        }

        private static void ThrowIfNotInitialized()
        {
            if (_ShuttingDown)
            {
                throw new InvalidOperationException("DataRuntime is shutting down.");
            }
            if (_ProviderFactory == null)
            {
                throw new InvalidOperationException("Call DataRuntime.Initialize before using data tables.");
            }
        }

    }
}
