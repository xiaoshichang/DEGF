using DE.Share.Data;

namespace Demo.Share.Data
{
    [DataTable("ItemData", Load = DataLoadPolicy.Row, Cache = DataCachePolicy.Lru, CacheCapacity = 1024)]
    public sealed partial class ItemDataRow : DataRow
    {
        public string Name { get; private set; }

        public int StackLimit { get; private set; }
    }
}
