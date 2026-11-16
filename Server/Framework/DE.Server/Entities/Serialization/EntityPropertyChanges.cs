using System;
using System.Collections.Generic;

namespace DE.Server.Entities
{
    /// <summary>一次属性状态更新的不可变编码副本；不表示逐次赋值历史，路径和字节不会引用调用方的可变数组。</summary>
    public sealed class EntityPropertyChange
    {
        private readonly byte[] _value;

        internal EntityPropertyChange(
            ulong sequence, IReadOnlyList<ulong> path, byte typeCode, byte[] value, EntitySerializationProfileMask profiles)
        {
            if (path == null || path.Count == 0)
            {
                throw new ArgumentException("A property change requires a nonempty path.", nameof(path));
            }
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            var pathCopy = new ulong[path.Count];
            for (int index = 0; index < pathCopy.Length; index++)
            {
                pathCopy[index] = path[index];
            }
            Path = Array.AsReadOnly(pathCopy);
            _value = (byte[])value.Clone();
            Sequence = sequence;
            TypeCode = typeCode;
            Profiles = profiles;
        }

        public ulong Sequence { get; }
        public IReadOnlyList<ulong> Path { get; }
        public byte TypeCode { get; }
        public ReadOnlyMemory<byte> Value => (byte[])_value.Clone();
        internal byte[] EncodedValue => _value;
        public EntitySerializationProfileMask Profiles { get; }
    }
}
