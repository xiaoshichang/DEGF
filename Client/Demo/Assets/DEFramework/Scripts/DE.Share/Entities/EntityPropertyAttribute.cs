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
        AllClients = 1 << 3,
        Persistent = 1 << 4,
    }

    /// <summary>
    /// 业务仅在 partial 类型的私有 __ 前缀字段上声明同步属性，不允许标记手写属性或自动属性。
    /// 公开属性上的标记由 SG 生成，保留供跨程序集属性分析使用。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class EntityPropertyAttribute : Attribute
    {
        public EntityPropertyAttribute(EntityPropertyFlag flags)
        {
            _Flags = flags;
        }

        private readonly EntityPropertyFlag _Flags;

        public EntityPropertyFlag Flags => _Flags;

        public bool HasFlag(EntityPropertyFlag flag)
        {
            return (_Flags & flag) == flag;
        }
    }
}
