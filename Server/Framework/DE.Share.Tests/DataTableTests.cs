using System;
using System.Collections.Generic;
using System.IO;
using DE.Share.Data;
using DE.Share.Data.DataDescribe;
using DE.Share.Data.DataProvider;
using Xunit;

namespace DE.Share.Tests
{
    [CollectionDefinition("DataRuntime", DisableParallelization = true)]
    public sealed class DataRuntimeCollectionDefinition
    {
    }

    [Collection("DataRuntime")]
    public sealed class DataTableTests : IDisposable
    {
        public DataTableTests()
        {
            DataRuntime.Shutdown();
        }

        public void Dispose()
        {
            DataRuntime.Shutdown();
        }

        [Fact]
        public void DataTableKey_RoundTripsInt32AndUsesValueEquality()
        {
            var first = DataTableKey.FromInt32(42);
            var second = DataTableKey.FromInt32(42);
            var other = DataTableKey.FromInt32(7);

            Assert.Equal(42, first.AsInt32());
            Assert.Equal(first, second);
            Assert.True(first == second);
            Assert.True(first != other);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
        }

        [Fact]
        public void DataTableDescribe_ValidatesPoliciesKeyAndColumnUniqueness()
        {
            var describe = CreateDescribe("ValidDescribe", DataLoadPolicy.Row, DataCachePolicy.Lru, 8);

            Assert.Equal(DataLoadPolicy.Row, describe.Load);
            Assert.Equal(DataCachePolicy.Lru, describe.Cache);
            Assert.Equal(8, describe.CacheCapacity);
            Assert.True(describe.Columns[0].IsKey);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateDescribe("InvalidLoad", (DataLoadPolicy)99, DataCachePolicy.KeepAlive, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateDescribe("InvalidCache", DataLoadPolicy.Full, (DataCachePolicy)99, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateDescribe("InvalidCapacity", DataLoadPolicy.Full, DataCachePolicy.KeepAlive, 0));
            Assert.Throws<ArgumentException>(() => new DataTableDescribe(
                "MissingKey",
                "MissingKey",
                "Data",
                nameof(TestRow),
                new DataColumnDescribe("Name", "Name", DataValueKind.String)
            ));
            Assert.Throws<ArgumentException>(() => new DataTableDescribe(
                "DuplicateColumn",
                "DuplicateColumn",
                "Data",
                nameof(TestRow),
                new DataColumnDescribe("Id", "Id", DataValueKind.Int32, isKey: true),
                new DataColumnDescribe("Id", "Name", DataValueKind.String)
            ));
        }

        [Fact]
        public void FullKeepAlive_LoadsAllRowsEagerlyAndKeepsRowIdentity()
        {
            var factory = InitializeRuntime(DefaultRows());

            var table = DataRuntime.GetTable<FullKeepAliveTable>();
            var provider = factory.SingleProvider;
            var first = table.GetRow(Key(1));

            Assert.Equal(3, table.Count);
            Assert.Equal("One", first.Name);
            Assert.Same(first, table.GetRow(Key(1)));
            Assert.Equal(1, provider.OpenCount);
            Assert.Equal(1, provider.Reader.ResetCount);
            Assert.Equal(3, provider.Reader.SuccessfulReadCount);
            Assert.Equal(3, provider.Reader.MaterializedRowCount);
        }

        [Fact]
        public void RowKeepAlive_CountDoesNotMaterializeAndKnownCountMakesPointLookupStopEarly()
        {
            var factory = InitializeRuntime(DefaultRows());
            var table = DataRuntime.GetTable<RowKeepAliveTable>();
            var provider = factory.SingleProvider;

            Assert.Equal(0, provider.OpenCount);
            Assert.Equal(3, table.Count);
            Assert.Equal(1, provider.OpenCount);
            Assert.Equal(1, provider.Reader.ResetCount);
            Assert.Equal(3, provider.Reader.SuccessfulReadCount);
            Assert.Equal(0, provider.Reader.MaterializedRowCount);

            var second = table.GetRow(Key(2));

            Assert.Equal("Two", second.Name);
            Assert.Equal(2, provider.Reader.ResetCount);
            Assert.Equal(5, provider.Reader.SuccessfulReadCount);
            Assert.Equal(1, provider.Reader.MaterializedRowCount);

            Assert.Same(second, table.GetRow(Key(2)));
            Assert.Equal(2, provider.Reader.ResetCount);
            Assert.Equal(1, provider.Reader.MaterializedRowCount);
        }

        [Fact]
        public void RowLru_EvictsLeastRecentlyUsedRowAndRetainsRecentlyAccessedRow()
        {
            var factory = InitializeRuntime(DefaultRows());
            var table = DataRuntime.GetTable<RowLruTable>();
            var provider = factory.SingleProvider;

            var first = table.GetRow(Key(1));
            var second = table.GetRow(Key(2));
            Assert.Same(first, table.GetRow(Key(1)));

            var third = table.GetRow(Key(3));

            Assert.Equal("Three", third.Name);
            Assert.Same(first, table.GetRow(Key(1)));
            var reloadedSecond = table.GetRow(Key(2));
            Assert.NotSame(second, reloadedSecond);
            Assert.Equal(4, provider.Reader.MaterializedRowCount);
        }

        [Fact]
        public void FullLru_CacheMissMaterializesOnlyRequestedRow()
        {
            var factory = InitializeRuntime(DefaultRows());
            var table = DataRuntime.GetTable<FullLruTable>();
            var provider = factory.SingleProvider;

            Assert.Equal(3, provider.Reader.MaterializedRowCount);
            var third = table.GetRow(Key(3));

            Assert.Equal("Three", third.Name);
            Assert.Equal(4, provider.Reader.MaterializedRowCount);
            Assert.Equal(2, provider.Reader.ResetCount);
        }

        [Fact]
        public void Count_RejectsDuplicateKeysWithoutMaterializingRows()
        {
            var factory = InitializeRuntime(
                new SourceRow(1, "One", 10),
                new SourceRow(2, "Two", 20),
                new SourceRow(1, "Duplicate", 30)
            );
            var table = DataRuntime.GetTable<RowKeepAliveTable>();

            var exception = Assert.Throws<DataLoadException>(() => _ = table.Count);

            Assert.Equal(4, exception.RowNumber);
            Assert.Equal("Id", exception.ColumnName);
            Assert.Contains("Duplicate key '1'", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, factory.SingleProvider.Reader.MaterializedRowCount);
        }

        [Fact]
        public void GetRow_MissingKeyThrowsAfterRecordingCompleteCount()
        {
            var factory = InitializeRuntime(DefaultRows());
            var table = DataRuntime.GetTable<RowKeepAliveTable>();

            Assert.Throws<KeyNotFoundException>(() => table.GetRow(Key(99)));

            Assert.Equal(3, table.Count);
            Assert.Equal(1, factory.SingleProvider.Reader.ResetCount);
            Assert.Equal(0, factory.SingleProvider.Reader.MaterializedRowCount);
        }

        [Fact]
        public void Prewarm_AcceptsOnlyFullKeepAliveAndReusesLoadedTable()
        {
            var factory = InitializeRuntime(DefaultRows());

            Assert.Throws<InvalidOperationException>(() => DataRuntime.Prewarm<RowKeepAliveTable>());
            Assert.Empty(factory.Providers);

            var warmed = DataRuntime.Prewarm<FullKeepAliveTable>();

            Assert.Same(warmed, DataRuntime.Prewarm<FullKeepAliveTable>());
            Assert.Same(warmed, DataRuntime.GetTable<FullKeepAliveTable>());
            Assert.Single(factory.Providers);
        }

        [Fact]
        public void PrintTableStatistics_DiscoversAllTablesAndReportsRowsAndCachedRows()
        {
            InitializeRuntime(DefaultRows());
            var rowTable = DataRuntime.GetTable<RowKeepAliveTable>();
            var fullTable = DataRuntime.GetTable<FullKeepAliveTable>();
            rowTable.GetRow(Key(2));

            var report = DataRuntime.PrintTableStatistics();
            var lines = report.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            Assert.Equal(1, rowTable.CachedRowCount);
            Assert.Equal(3, fullTable.CachedRowCount);
            Assert.Equal(5, lines.Length);
            Assert.Equal(
                "DataRuntime table statistics: tableCount=4, rowCount=12, cachedRowCount=7.",
                lines[0]
            );
            Assert.Equal(
                $"tableName=FullKeepAliveTest, tableType={typeof(FullKeepAliveTable).FullName}, rowCount=3, cachedRowCount=3.",
                lines[1]
            );
            Assert.Equal(
                $"tableName=NpcData, tableType={typeof(NpcDataTable).FullName}, rowCount=3, cachedRowCount=0.",
                lines[2]
            );
            Assert.Equal(
                $"tableName=RowKeepAliveTest, tableType={typeof(RowKeepAliveTable).FullName}, rowCount=3, cachedRowCount=1.",
                lines[3]
            );
            Assert.Equal(
                $"tableName=SpaceData, tableType={typeof(SpaceDataTable).FullName}, rowCount=3, cachedRowCount=3.",
                lines[4]
            );
        }

        [Fact]
        public void DataRuntime_ValidatesConfigurationCachesTablesAndCanReinitialize()
        {
            Assert.Throws<InvalidOperationException>(() => DataRuntime.SetRootDirectory(Path.GetTempPath()));

            var firstFactory = new TestProviderFactory(DefaultRows());
            DataRuntime.Initialize(firstFactory.Create);
            Assert.Throws<InvalidOperationException>(() => DataRuntime.GetTable<RowKeepAliveTable>());
            DataRuntime.SetRootDirectory(Path.GetTempPath());
            Assert.Throws<InvalidOperationException>(() => DataRuntime.SetRootDirectory(Path.GetTempPath()));
            Assert.Throws<InvalidOperationException>(() => DataRuntime.Initialize(firstFactory.Create));

            var firstTable = DataRuntime.GetTable<RowKeepAliveTable>();
            Assert.Same(firstTable, DataRuntime.GetTable<RowKeepAliveTable>());

            DataRuntime.Shutdown();
            var secondFactory = InitializeRuntime(DefaultRows());
            var secondTable = DataRuntime.GetTable<RowKeepAliveTable>();

            Assert.NotSame(firstTable, secondTable);
            Assert.Single(firstFactory.Providers);
            Assert.Single(secondFactory.Providers);
        }

        [Fact]
        public void DataRuntime_RejectsDuplicateLogicalTableNamesAcrossTypes()
        {
            var factory = InitializeRuntime(DefaultRows());

            DataRuntime.GetTable<FirstDuplicateNameTable>();
            var exception = Assert.Throws<InvalidOperationException>(() =>
                DataRuntime.GetTable<SecondDuplicateNameTable>());

            Assert.Contains("DuplicateLogicalName", exception.Message, StringComparison.Ordinal);
            Assert.Single(factory.Providers);
        }

        private static TestProviderFactory InitializeRuntime(params SourceRow[] rows)
        {
            var factory = new TestProviderFactory(rows);
            DataRuntime.Initialize(factory.Create);
            DataRuntime.SetRootDirectory(Path.GetTempPath());
            return factory;
        }

        private static SourceRow[] DefaultRows()
        {
            return new[]
            {
                new SourceRow(1, "One", 10),
                new SourceRow(2, "Two", 20),
                new SourceRow(3, "Three", 30),
            };
        }

        private static DataTableKey Key(int value)
        {
            return DataTableKey.FromInt32(value);
        }

        private static DataTableDescribe CreateDescribe(
            string tableName,
            DataLoadPolicy load,
            DataCachePolicy cache,
            int cacheCapacity)
        {
            return new DataTableDescribe(
                tableName,
                tableName,
                "Data",
                nameof(TestRow),
                load,
                cache,
                cacheCapacity,
                new DataColumnDescribe("Id", "Id", DataValueKind.Int32, isKey: true),
                new DataColumnDescribe("Name", "Name", DataValueKind.String),
                new DataColumnDescribe("Score", "Score", DataValueKind.Int32)
            );
        }

        private sealed class TestRow : DataRow
        {
            public string Name { get; private set; }
            public int Score { get; private set; }

            public static TestRow Create(IDataTableReader reader)
            {
                return new TestRow
                {
                    Id = DataTableKey.FromInt32(reader.GetInt32(0)),
                    Name = reader.GetString(1),
                    Score = reader.GetInt32(2),
                };
            }
        }

        private abstract class TestTable : DataTable<TestRow>
        {
            protected TestTable(
                DataTableDescribe describe,
                Func<IDataProvider> providerFactory,
                string rootDirectory)
                : base(describe, providerFactory, rootDirectory, TestRow.Create)
            {
            }
        }

        private sealed class FullKeepAliveTable : TestTable
        {
            private static readonly DataTableDescribe s_describe = CreateDescribe(
                "FullKeepAliveTest",
                DataLoadPolicy.Full,
                DataCachePolicy.KeepAlive,
                8
            );

            static FullKeepAliveTable()
            {
                InitializeFactory<FullKeepAliveTable>(
                    s_describe,
                    (factory, root) => new FullKeepAliveTable(factory, root)
                );
            }

            private FullKeepAliveTable(Func<IDataProvider> providerFactory, string rootDirectory)
                : base(s_describe, providerFactory, rootDirectory)
            {
            }
        }

        private sealed class RowKeepAliveTable : TestTable
        {
            private static readonly DataTableDescribe s_describe = CreateDescribe(
                "RowKeepAliveTest",
                DataLoadPolicy.Row,
                DataCachePolicy.KeepAlive,
                8
            );

            static RowKeepAliveTable()
            {
                InitializeFactory<RowKeepAliveTable>(
                    s_describe,
                    (factory, root) => new RowKeepAliveTable(factory, root)
                );
            }

            private RowKeepAliveTable(Func<IDataProvider> providerFactory, string rootDirectory)
                : base(s_describe, providerFactory, rootDirectory)
            {
            }
        }

        private sealed class RowLruTable : TestTable
        {
            private static readonly DataTableDescribe s_describe = CreateDescribe(
                "RowLruTest",
                DataLoadPolicy.Row,
                DataCachePolicy.Lru,
                2
            );

            static RowLruTable()
            {
                InitializeFactory<RowLruTable>(
                    s_describe,
                    (factory, root) => new RowLruTable(factory, root)
                );
            }

            private RowLruTable(Func<IDataProvider> providerFactory, string rootDirectory)
                : base(s_describe, providerFactory, rootDirectory)
            {
            }
        }

        private sealed class FullLruTable : TestTable
        {
            private static readonly DataTableDescribe s_describe = CreateDescribe(
                "FullLruTest",
                DataLoadPolicy.Full,
                DataCachePolicy.Lru,
                2
            );

            static FullLruTable()
            {
                InitializeFactory<FullLruTable>(
                    s_describe,
                    (factory, root) => new FullLruTable(factory, root)
                );
            }

            private FullLruTable(Func<IDataProvider> providerFactory, string rootDirectory)
                : base(s_describe, providerFactory, rootDirectory)
            {
            }
        }

        private sealed class FirstDuplicateNameTable : TestTable
        {
            private static readonly DataTableDescribe s_describe = CreateDescribe(
                "DuplicateLogicalName",
                DataLoadPolicy.Row,
                DataCachePolicy.KeepAlive,
                8
            );

            static FirstDuplicateNameTable()
            {
                InitializeFactory<FirstDuplicateNameTable>(
                    s_describe,
                    (factory, root) => new FirstDuplicateNameTable(factory, root)
                );
            }

            private FirstDuplicateNameTable(Func<IDataProvider> providerFactory, string rootDirectory)
                : base(s_describe, providerFactory, rootDirectory)
            {
            }
        }

        private sealed class SecondDuplicateNameTable : TestTable
        {
            private static readonly DataTableDescribe s_describe = CreateDescribe(
                "DuplicateLogicalName",
                DataLoadPolicy.Row,
                DataCachePolicy.KeepAlive,
                8
            );

            static SecondDuplicateNameTable()
            {
                InitializeFactory<SecondDuplicateNameTable>(
                    s_describe,
                    (factory, root) => new SecondDuplicateNameTable(factory, root)
                );
            }

            private SecondDuplicateNameTable(Func<IDataProvider> providerFactory, string rootDirectory)
                : base(s_describe, providerFactory, rootDirectory)
            {
            }
        }

        private readonly struct SourceRow
        {
            public SourceRow(int id, string name, int score)
            {
                Id = id;
                Name = name;
                Score = score;
            }

            public int Id { get; }
            public string Name { get; }
            public int Score { get; }
        }

        private sealed class TestProviderFactory
        {
            private readonly SourceRow[] _rows;

            public TestProviderFactory(SourceRow[] rows)
            {
                _rows = (SourceRow[])rows.Clone();
            }

            public List<InMemoryDataProvider> Providers { get; } = new List<InMemoryDataProvider>();

            public InMemoryDataProvider SingleProvider => Assert.Single(Providers);

            public IDataProvider Create()
            {
                var provider = new InMemoryDataProvider(_rows);
                Providers.Add(provider);
                return provider;
            }
        }

        private sealed class InMemoryDataProvider : IDataProvider
        {
            private readonly SourceRow[] _rows;

            public InMemoryDataProvider(SourceRow[] rows)
            {
                _rows = (SourceRow[])rows.Clone();
            }

            public int OpenCount { get; private set; }
            public InMemoryDataTableReader Reader { get; private set; }

            public IDataTableReader OpenTable(string rootDirectory, DataTableDescribe describe)
            {
                OpenCount++;
                if (Reader == null)
                {
                    Reader = new InMemoryDataTableReader(_rows, describe);
                }

                return Reader;
            }

            public void Dispose()
            {
            }
        }

        private sealed class InMemoryDataTableReader : IDataTableReader
        {
            private readonly SourceRow[] _rows;
            private readonly DataTableDescribe _describe;
            private int _index = -1;

            public InMemoryDataTableReader(SourceRow[] rows, DataTableDescribe describe)
            {
                _rows = (SourceRow[])rows.Clone();
                _describe = describe;
                SourcePath = $"memory://{describe.TableName}";
                SheetName = describe.SheetName;
            }

            public string SourcePath { get; }
            public string SheetName { get; }
            public long RowNumber { get; private set; }
            public int ResetCount { get; private set; }
            public int SuccessfulReadCount { get; private set; }
            public int MaterializedRowCount { get; private set; }

            public void Reset()
            {
                _index = -1;
                RowNumber = 0;
                ResetCount++;
            }

            public bool Read()
            {
                var next = _index + 1;
                if (next >= _rows.Length)
                {
                    _index = _rows.Length;
                    return false;
                }

                _index = next;
                RowNumber = _index + 2L;
                SuccessfulReadCount++;
                return true;
            }

            public bool IsNull(int columnIndex)
            {
                return GetValue(columnIndex) == null;
            }

            public int GetInt32(int columnIndex)
            {
                return Convert.ToInt32(GetValue(columnIndex));
            }

            public uint GetUInt32(int columnIndex)
            {
                return Convert.ToUInt32(GetValue(columnIndex));
            }

            public long GetInt64(int columnIndex)
            {
                return Convert.ToInt64(GetValue(columnIndex));
            }

            public ulong GetUInt64(int columnIndex)
            {
                return Convert.ToUInt64(GetValue(columnIndex));
            }

            public float GetSingle(int columnIndex)
            {
                return Convert.ToSingle(GetValue(columnIndex));
            }

            public double GetDouble(int columnIndex)
            {
                return Convert.ToDouble(GetValue(columnIndex));
            }

            public bool GetBoolean(int columnIndex)
            {
                return Convert.ToBoolean(GetValue(columnIndex));
            }

            public string GetString(int columnIndex)
            {
                MaterializedRowCount++;
                return Convert.ToString(GetValue(columnIndex));
            }

            public TEnum GetEnum<TEnum>(int columnIndex) where TEnum : struct, Enum
            {
                var value = GetValue(columnIndex);
                if (value is string name)
                {
                    return (TEnum)Enum.Parse(typeof(TEnum), name, false);
                }

                return (TEnum)Enum.ToObject(typeof(TEnum), value);
            }

            private object GetValue(int columnIndex)
            {
                if (_index < 0 || _index >= _rows.Length)
                {
                    throw new InvalidOperationException("Call Read successfully before reading a value.");
                }

                var row = _rows[_index];
                switch (_describe.Columns[columnIndex].PropertyName)
                {
                case "Id":
                    return row.Id;
                case "Name":
                    return row.Name;
                case "Score":
                case "MaxPlayers":
                case "Attack":
                case "Level":
                case "MaxHealth":
                    return row.Score;
                default:
                    throw new ArgumentOutOfRangeException(nameof(columnIndex));
                }
            }
        }
    }
}
