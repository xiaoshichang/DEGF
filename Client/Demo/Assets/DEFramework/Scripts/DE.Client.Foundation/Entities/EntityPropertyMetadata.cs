using System;

namespace DE.Client.Entities
{
    /// <summary>SG 生成的同步属性描述；快照通过字段委托读写，不触发公开属性回调。</summary>
    public sealed class EntityPropertyMetadata
    {
        private readonly Func<object, object> _getter;
        private readonly Action<object, object> _setter;

        public EntityPropertyMetadata(
            ulong id,
            string name,
            Type valueType,
            EntityPropertyFlag flags,
            Func<object, object> getter,
            Action<object, object> setter)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
            Flags = flags;
            _getter = getter ?? throw new ArgumentNullException(nameof(getter));
            _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        }

        public ulong Id { get; }
        public string Name { get; }
        public Type ValueType { get; }
        public EntityPropertyFlag Flags { get; }

        public object GetValue(object target)
        {
            return _getter(target);
        }

        public void SetValue(object target, object value)
        {
            _setter(target, value);
        }
    }
}
