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
        private IDataTableReader _Reader;
        private string _RootDirectory;
        private int? _Count;
        private bool _IsLoading;
        private ExceptionDispatchInfo _LoadFailure;

        protected DataTable(DataTableDescribe describe, Func<IDataProvider> providerFactory, string rootDirectory,
            Func<IDataTableReader, TRow> materialize)
        {
            Describe = describe ?? throw new ArgumentNullException(nameof(describe));
            if (providerFactory == null)
            {
                throw new ArgumentNullException(nameof(providerFactory));
            }
            _RootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
            _Materialize = materialize ?? throw new ArgumentNullException(nameof(materialize));
            _Rows = new DataRowCache<TRow>(describe.Cache, describe.CacheCapacity);
            _Provider = providerFactory() ?? throw new InvalidOperationException("The data provider factory returned null.");
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
                        ScanRows(null, false, out int count);
                        _Count = count;
                    }
                    catch (Exception error)
                    {
                        RecordLoadFailure(error);
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

        protected static void InitializeFactory<TTable>(DataTableDescribe describe, Func<Func<IDataProvider>, string, TTable> loader)
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
            DisposeProvider();
        }

        private void LoadRows(DataTableKey? key)
        {
            ThrowIfUnavailable();
            ThrowIfDetached();
            _IsLoading = true;
            try
            {
                var rows = ScanRows(key, true, out int count);
                // Publish only after the complete scan has succeeded; the provider keeps the reader open.
                _Rows.AddRows(rows, key);
                _Count = count;
            }
            catch (Exception error)
            {
                RecordLoadFailure(error);
                throw;
            }
            finally
            {
                _IsLoading = false;
            }
        }

        private Dictionary<DataTableKey, TRow> ScanRows(DataTableKey? key, bool materialize, out int count)
        {
            if (_Reader == null)
            {
                _Reader = _Provider.OpenTable(_RootDirectory, Describe)
                    ?? throw new InvalidOperationException("The data provider returned a null reader.");
            }
            var rows = new Dictionary<DataTableKey, TRow>();
            var positions = new Dictionary<DataTableKey, long>();
            try
            {
                // Reset also makes the first scan independent of the provider's current cursor position.
                _Reader.Reset();
                while (_Reader.Read())
                {
                    var sourceKey = DataTableKey.FromInt32(_Reader.GetInt32(0));
                    if (positions.TryGetValue(sourceKey, out long previousRow))
                    {
                        throw new DataLoadException($"Duplicate key '{sourceKey}' in table '{Describe.TableName}'; first seen at row {previousRow}.",
                            _Reader.SourcePath, _Reader.SheetName, _Reader.RowNumber, Describe.Columns[0].ColumnName);
                    }
                    positions.Add(sourceKey, _Reader.RowNumber);
                    if (materialize && (Describe.Load == DataLoadPolicy.Full || key == sourceKey))
                    {
                        var row = _Materialize(_Reader);
                        if (row == null || row.Id != sourceKey)
                        {
                            throw new InvalidOperationException("The row factory must return a non-null row with the source key.");
                        }
                        rows.Add(sourceKey, row);
                    }
                }
                count = positions.Count;
                return rows;
            }
            catch (Exception error) when (!(error is DataLoadException))
            {
                throw new DataLoadException($"Could not load table '{Describe.TableName}': {error.Message}",
                    _Reader.SourcePath, _Reader.SheetName, _Reader.RowNumber, innerException: error);
            }
        }

        private void RecordLoadFailure(Exception error)
        {
            _LoadFailure = ExceptionDispatchInfo.Capture(error);
            try
            {
                DisposeProvider();
            }
            catch (Exception disposeError)
            {
                // Cleanup must not replace the first failure that subsequent reads rethrow.
                error.Data["DataProvider.DisposeException"] = disposeError;
            }
        }

        private void DisposeProvider()
        {
            var provider = _Provider;
            _Provider = null;
            _Reader = null;
            _RootDirectory = null;
            provider?.Dispose();
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
