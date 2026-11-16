namespace DE.Server.Entities
{
    public static class EntitySerializationLimits
    {
        public const int MaximumPacketBytes = 4 * 1024 * 1024;
        public const int MaximumValueBytes = 1024 * 1024;
        public const int MaximumDepth = 16;
        public const int MaximumEntries = ushort.MaxValue;
    }
}
