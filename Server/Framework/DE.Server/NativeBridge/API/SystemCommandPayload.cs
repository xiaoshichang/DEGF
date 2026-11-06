using System;
using System.Buffers.Binary;
using System.Text;

namespace DE.Server.NativeBridge
{
    public enum SystemCommandKind : ushort
    {
        AvatarMigration = 1,
        CreateEntity = 2,
        CreateAvatar = 3,
        CreateAvatarResponse = 4,
    }

    public struct SystemCommandPayload
    {
        public const ushort CurrentVersion = 2;
        public const int FixedWireSize = 30;
        public const int MaximumDataSize = 16 * 1024 * 1024;

        public ushort Version;
        public SystemCommandKind Kind;
        public Guid EntityId;
        public string TargetServerId;
        public uint CommandId;
        public byte[] Data;

        public byte[] Serialize()
        {
            byte[] target = Encoding.UTF8.GetBytes(TargetServerId ?? string.Empty);
            byte[] data = Data ?? Array.Empty<byte>();
            if (Version != CurrentVersion || !Enum.IsDefined(typeof(SystemCommandKind), Kind)
                || target.Length == 0 || target.Length > ushort.MaxValue || data.Length > MaximumDataSize)
            {
                throw new InvalidOperationException("Invalid system command envelope.");
            }

            var bytes = new byte[FixedWireSize + target.Length + data.Length];
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), Version);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)Kind);
            EntityId.TryWriteBytes(bytes.AsSpan(4, 16));
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(20), (ushort)target.Length);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(22), CommandId);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(26), (uint)data.Length);
            target.CopyTo(bytes, FixedWireSize);
            data.CopyTo(bytes, FixedWireSize + target.Length);
            return bytes;
        }

        public static bool TryDeserialize(byte[] bytes, int offset, int size, out SystemCommandPayload command)
        {
            command = default;
            if (bytes == null || offset < 0 || size < FixedWireSize || offset > bytes.Length - size)
            {
                return false;
            }
            var source = bytes.AsSpan(offset, size);
            ushort version = BinaryPrimitives.ReadUInt16BigEndian(source);
            var kind = (SystemCommandKind)BinaryPrimitives.ReadUInt16BigEndian(source.Slice(2));
            int targetLength = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(20));
            uint dataLength = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(26));
            if (version != CurrentVersion || !Enum.IsDefined(typeof(SystemCommandKind), kind) || targetLength == 0
                || dataLength > MaximumDataSize || (long)FixedWireSize + targetLength + dataLength != size)
            {
                return false;
            }
            command = new SystemCommandPayload
            {
                Version = version,
                Kind = kind,
                EntityId = new Guid(source.Slice(4, 16)),
                TargetServerId = Encoding.UTF8.GetString(source.Slice(FixedWireSize, targetLength)),
                CommandId = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(22)),
                Data = source.Slice(FixedWireSize + targetLength, (int)dataLength).ToArray(),
            };
            return true;
        }
    }
}
