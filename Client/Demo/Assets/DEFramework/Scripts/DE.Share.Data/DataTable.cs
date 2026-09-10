using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using DE.Share.Data.DataDescribe;
using DE.Share.Data.DataProvider;

namespace DE.Share.Data
{
    /// <summary>A single-threaded table handle. Loading and row retention follow its generated description.</summary>
    public abstract class DataTable<TRow> : IDataTable, IDataTableState where TRow : DataRow
    {
        private readonly DataRowCache<TRow> _Rows;
        private readonly Func<IDataTableReader, TRow> _Materialize;
        private IDataProvider _Provider;
        private string _RootDirectory;
        private int? _Count;
        private bool _IsLoading;
        private ExceptionDispatchInfo _LoadFailure;

        protected DataTable(DataTableDescribe describe, IDataProvider provider, string rootDirectory,
            Func<IDataTableReader, TRow> materialize)
        {
            Describe = describe ?? throw new ArgumentNullException(nameof(describe));
            _Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _RootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
            _Materialize = materialize ?? throw new ArgumentNullException(nameof(materialize));
            _Rows = new DataRowCache<TRow>(describe.Cache, describe.CacheCapacity);
            if (describe.Load == DataLoadPolicy.Full)
            {
                LoadRows(null);
            }
        }

        public DataTableDescribe Describe { get; }

        /// <summary>Total source rows, independent of cache capacity. The first access may scan the source.</summary>
        public int Count
        {
            get
            {
                ThrowIfUnavailable();
                if (!_Count.HasValue)
                {
                    ThrowIfDetached();
                    _IsLoading = true;
                    try
                    {
                        _Count = DataTableLoader.CountRows(_Provider, _RootDirectory, Describe);
                    }
                    catch (Exception error)
                    {
                        _LoadFailure = ExceptionDispatchInfo.Capture(error);
                        throw;
                    }
                    finally
                    {
                        _IsLoading = false;
                    }
                }
                return _Count.Value;
            }
        }

        protected static void InitializeFactory<TTable>(DataTableDescribe describe, Func<IDataProvider, string, TTable> loader)
            where TTable : DataTable<TRow>
        {
            DataTableFactory<TTable>.Initialize(describe, loader);
        }

        public TRow GetRow(DataTableKey key)
        {
            ThrowIfUnavailable();
            if (_Rows.TryGetRow(key, out var row))
            {
                return row;
            }
            if (Describe.Load != DataLoadPolicy.Full || Describe.Cache != DataCachePolicy.KeepAlive)
            {
                LoadRows(key);
                if (_Rows.TryGetRow(key, out row))
                {
                    return row;
                }
            }
            throw new KeyNotFoundException($"Table '{Describe.TableName}' does not contain key '{key}'.");
        }

        bool IDataTableState.IsLoading => _IsLoading;

        void IDataTableState.ThrowIfUnavailable()
        {
            ThrowIfUnavailable();
        }

        void IDataTableState.Detach()
        {
            _Provider = null;
            _RootDirectory = null;
        }

        private void LoadRows(DataTableKey? key)
        {
            ThrowIfUnavailable();
            ThrowIfDetached();
            _IsLoading = true;
            try
            {
                var rows = DataTableLoader.LoadRows(_Provider, _RootDirectory, Describe, _Materialize,
                    CreateFilter(key), out int count);
                // Publish only after the complete scan and disposal have succeeded.
                _Rows.AddRows(rows, key);
                _Count = count;
            }
            catch (Exception error)
            {
                _LoadFailure = ExceptionDispatchInfo.Capture(error);
                throw;
            }
            finally
            {
                _IsLoading = false;
            }
        }

        private Func<DataTableKey, bool> CreateFilter(DataTableKey? key)
        {
            if (Describe.Load == DataLoadPolicy.Full)
            {
                return null;
            }
            var requested = key.Value;
            return candidate => candidate == requested;
        }

        private void ThrowIfUnavailable()
        {
            _LoadFailure?.Throw();
            if (_IsLoading)
            {
                throw new InvalidOperationException($"Recursive loading of table '{Describe.TableName}' is not supported.");
            }
        }

        private void ThrowIfDetached()
        {
            if (_Provider == null)
            {
                throw new InvalidOperationException($"Table '{Describe.TableName}' cannot load more rows after its DataRuntime has shut down.");
            }
        }
    }
}
