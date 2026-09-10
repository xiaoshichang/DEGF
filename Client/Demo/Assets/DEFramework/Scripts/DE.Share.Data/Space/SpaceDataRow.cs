namespace DE.Share.Data
{
    [DataTable("SpaceData", Load = DataLoadPolicy.Full, Cache = DataCachePolicy.KeepAlive)]
    public sealed partial class SpaceDataRow : DataRow
    {
        public string Name { get; private set; }

        public int MaxPlayers { get; private set; }
    }
}
