using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using DE.Share.Data.DataProvider;

namespace DE.Share.Data
{
    /// <summary>
    /// Shares data configuration and cached tables across the process. Use all members on one thread.
    /// </summary>
    public static class DataRuntime
    {
        private static readonly Dictionary<Type, IDataTable> _Tables = new Dictionary<Type, IDataTable>();
        private static readonly Dictionary<Type, ExceptionDispatchInfo> _LoadFailures = new Dictionary<Type, ExceptionDispatchInfo>();
        private static readonly Dictionary<string, Type> _TableNames = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly HashSet<Type> _Loading = new HashSet<Type>();
        private static IDataProvider _Provider;
        private static string _RootDirectory;

        public static void Initialize(IDataProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (_Provider != null)
            {
                throw new InvalidOperationException("DataRuntime is already initialized. Call Shutdown before initializing it again.");
            }
            _Provider = provider;
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
                var table = DataTableFactory<TTable>.Load(_Provider, _RootDirectory);
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

        public static void Shutdown()
        {
            if (_Loading.Count != 0)
            {
                throw new InvalidOperationException("DataRuntime cannot shut down while a table is loading.");
            }
            _Tables.Clear();
            _LoadFailures.Clear();
            _TableNames.Clear();
            _Provider = null;
            _RootDirectory = null;
        }

        private static void ThrowIfNotInitialized()
        {
            if (_Provider == null)
            {
                throw new InvalidOperationException("Call DataRuntime.Initialize before using data tables.");
            }
        }

    }
}
