using System;
using System.Collections.Generic;
using DE.Share.Data.DataDescribe;
using DE.Share.Data.DataProvider;

namespace DE.Share.Data
{
    /// <summary>A single-threaded table handle. Loading and row retention follow its generated description.</summary>
    public abstract class DataTable<TRow> : IDataTable where TRow : DataRow
    {
        private readonly DataRowCache<TRow> _Rows;
        private readonly Func<IDataTableReader, TRow> _Materialize;
        private readonly IDataProvider _Provider;
        private IDataTableReader _Reader;
        private readonly string _RootDirectory;
        private int? _Count;

        protected DataTable(DataTableDescribe describe, Func<IDataProvider> providerFactory, string rootDirectory, Func<IDataTableReader, TRow> materialize)
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
                if (!_Count.HasValue)
                {
                    ScanRows(null, false, out int count);
                    _Count = count;
                }
                return _Count.Value;
            }
        }

        protected static void InitializeFactory<TTable>(DataTableDescribe describe, Func<Func<IDataProvider>, string, TTable> loader) where TTable : DataTable<TRow>
        {
            DataTableFactory<TTable>.Initialize(describe, loader);
        }

        public TRow GetRow(DataTableKey key)
        {
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

        private void LoadRows(DataTableKey? key)
        {
            var rows = ScanRows(key, true, out int count);
            _Rows.AddRows(rows, key);
            _Count = count;
        }

        /// <summary>
        /// Scans the source from the first data row and optionally materializes rows.
        /// When <paramref name="materialize"/> is true, a null <paramref name="key"/> materializes every
        /// row, while a specified key materializes only the matching row. Otherwise, the scan only validates
        /// keys and calculates the total source row count.
        /// </summary>
        /// <remarks>
        /// The first successful scan always reaches the end of the source so duplicate keys are detected
        /// and <paramref name="count"/> is authoritative. Once the count is known, later point lookups may
        /// return immediately after materializing the requested row because the same open source has already
        /// passed the full key scan.
        /// </remarks>
        /// <param name="key">The row key to materialize, or null to materialize all rows.</param>
        /// <param name="materialize">Whether matching source rows should be converted into <typeparamref name="TRow"/> objects.</param>
        /// <param name="count">Receives the total number of unique source rows.</param>
        /// <returns>The materialized rows. The dictionary is empty for a count-only scan or when the requested key does not exist.</returns>
        /// <exception cref="DataLoadException">The source cannot be read, contains duplicate keys, or produces an invalid row.</exception>
        /// <exception cref="InvalidOperationException">The data provider returns a null reader.</exception>
        private Dictionary<DataTableKey, TRow> ScanRows(DataTableKey? key, bool materialize, out int count)
        {
            if (_Reader == null)
            {
                _Reader = _Provider.OpenTable(_RootDirectory, Describe) ?? throw new InvalidOperationException("The data provider returned a null reader.");
            }

            var knownCount = _Count;
            var isPointLookup = materialize && key.HasValue;
            var canStopWhenFound = isPointLookup && knownCount.HasValue;
            var rowCapacity = isPointLookup ? 1 : knownCount.GetValueOrDefault();
            var rows = new Dictionary<DataTableKey, TRow>(rowCapacity);
            var positions = canStopWhenFound ? null : new Dictionary<DataTableKey, long>(knownCount.GetValueOrDefault());
            try
            {
                // The provider owns and reuses the reader, so every independent scan must reset its cursor.
                _Reader.Reset();
                while (_Reader.Read())
                {
                    var sourceKey = DataTableKey.FromInt32(_Reader.GetInt32(0));
                    if (positions != null)
                    {
                        if (positions.TryGetValue(sourceKey, out long previousRow))
                        {
                            throw new DataLoadException(
                                $"Duplicate key '{sourceKey}' in table '{Describe.TableName}'; first seen at row {previousRow}.",
                                _Reader.SourcePath,
                                _Reader.SheetName,
                                _Reader.RowNumber,
                                Describe.Columns[0].ColumnName
                            );
                        }

                        positions.Add(sourceKey, _Reader.RowNumber);
                    }

                    if (!materialize)
                    {
                        continue;
                    }

                    if (key.HasValue && key.Value != sourceKey)
                    {
                        continue;
                    }

                    var row = _Materialize(_Reader);
                    if (row == null)
                    {
                        throw new InvalidOperationException("The row factory returned null.");
                    }

                    if (row.Id != sourceKey)
                    {
                        throw new InvalidOperationException("The row factory returned a row whose key does not match the source key.");
                    }

                    rows.Add(sourceKey, row);
                    if (canStopWhenFound)
                    {
                        count = knownCount.Value;
                        return rows;
                    }
                }

                if (knownCount.HasValue)
                {
                    count = knownCount.Value;
                }
                else
                {
                    count = positions.Count;
                }

                return rows;
            }
            catch (Exception error) when (!(error is DataLoadException))
            {
                throw new DataLoadException(
                    $"Could not load table '{Describe.TableName}': {error.Message}",
                    _Reader.SourcePath,
                    _Reader.SheetName,
                    _Reader.RowNumber,
                    innerException: error
                );
            }
        }

    }
}
