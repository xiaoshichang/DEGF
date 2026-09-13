namespace DE.Share.Data
{
    [DataTable("NpcData", Load = DataLoadPolicy.Row, Cache = DataCachePolicy.Lru, CacheCapacity = 3)]
    public sealed partial class NpcDataRow : DataRow
    {
        public string Name { get; private set; }

        public int Level { get; private set; }

        public int MaxHealth { get; private set; }

        public int Attack { get; private set; }
    }
}
