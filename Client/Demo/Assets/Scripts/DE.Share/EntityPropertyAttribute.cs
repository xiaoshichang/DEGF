using System;

namespace DE.Share.Entities
{
    [Flags]
    public enum EntityPropertyFlag : byte
    {
        None = 0,
        ServerOnly = 1 << 0,
        ClientOnly = 1 << 1,
        ClientServer = 1 << 2,
        Persistent = 1 << 3,
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class EntityPropertyAttribute : Attribute
    {
        public EntityPropertyAttribute(EntityPropertyFlag flags)
        {
            _Flags = flags;
        }

        private readonly EntityPropertyFlag _Flags;

        public bool HasFlag(EntityPropertyFlag flag)
        {
            return (_Flags & flag) == flag;
        }
    }
}
