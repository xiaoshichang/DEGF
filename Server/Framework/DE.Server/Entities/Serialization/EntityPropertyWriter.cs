using System;
using System.IO;
using System.Text;
using DE.Share.Foundation.Mathematics;

namespace DE.Server.Entities
{
    /// <summary>生成代码使用的强类型写入接口；整数使用大端序，不进行运行时属性类型判断。</summary>
    public sealed class EntityPropertyWriter
    {
        private readonly MemoryStream _stream = new MemoryStream();

        public int Length => checked((int)_stream.Length);
        public ushort PropertyCount { get; private set; }

        public void WritePropertyHeader(ulong id, byte typeCode, int valueLength)
        {
            if (valueLength < 0 || valueLength > (typeCode == 0
                ? EntitySerializationLimits.MaximumPacketBytes : EntitySerializationLimits.MaximumValueBytes))
            {
                throw new ArgumentOutOfRangeException(nameof(valueLength));
            }
            PropertyCount = checked((ushort)(PropertyCount + 1));
            WriteUInt64(id);
            WriteByte(typeCode);
            WriteUInt32((uint)valueLength);
        }

        public byte[] ToArray() => _stream.ToArray();
        public void WriteBytes(byte[] value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            if (value.Length > EntitySerializationLimits.MaximumPacketBytes - Length)
            {
                throw new InvalidOperationException("The property packet exceeds its size limit.");
            }
            _stream.Write(value, 0, value.Length);
        }

        public void WriteByte(byte value)
        {
            if (Length >= EntitySerializationLimits.MaximumPacketBytes)
            {
                throw new InvalidOperationException("The property packet exceeds its size limit.");
            }
            _stream.WriteByte(value);
        }
        public void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);
        public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));
        public void WriteChar(char value) => WriteUInt16(value);
        public void WriteInt16(short value) => WriteUInt16(unchecked((ushort)value));
        public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));
        public void WriteInt64(long value) => WriteUInt64(unchecked((ulong)value));

        public void WriteUInt16(ushort value)
        {
            WriteByte((byte)((value >> 8) & 0xff));
            WriteByte((byte)(value & 0xff));
        }

        public void WriteUInt32(uint value)
        {
            WriteByte((byte)((value >> 24) & 0xff));
            WriteByte((byte)((value >> 16) & 0xff));
            WriteByte((byte)((value >> 8) & 0xff));
            WriteByte((byte)(value & 0xff));
        }

        public void WriteUInt64(ulong value)
        {
            WriteByte((byte)((value >> 56) & 0xff));
            WriteByte((byte)((value >> 48) & 0xff));
            WriteByte((byte)((value >> 40) & 0xff));
            WriteByte((byte)((value >> 32) & 0xff));
            WriteByte((byte)((value >> 24) & 0xff));
            WriteByte((byte)((value >> 16) & 0xff));
            WriteByte((byte)((value >> 8) & 0xff));
            WriteByte((byte)(value & 0xff));
        }

        public void WriteSingle(float value) => WriteUInt32(unchecked((uint)BitConverter.ToInt32(BitConverter.GetBytes(value), 0)));
        public void WriteDouble(double value) => WriteUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
        public void WriteGuid(Guid value) => WriteBytes(value.ToByteArray());

        public void WriteString(string value)
        {
            GetStringWireSize(value);
            WriteByte(value == null ? (byte)0 : (byte)1);
            if (value != null)
            {
                WriteBytes(Encoding.UTF8.GetBytes(value));
            }
        }

        public void WriteVector3(Vector3 value)
        {
            WriteSingle(value.X);
            WriteSingle(value.Y);
            WriteSingle(value.Z);
        }

        public void WriteQuaternion(Quaternion value)
        {
            WriteSingle(value.X);
            WriteSingle(value.Y);
            WriteSingle(value.Z);
            WriteSingle(value.W);
        }

        public static int GetStringWireSize(string value)
        {
            int length = value == null ? 0 : Encoding.UTF8.GetByteCount(value);
            if (length >= EntitySerializationLimits.MaximumValueBytes)
            {
                throw new InvalidOperationException("Entity string property is too long.");
            }
            return sizeof(byte) + length;
        }
    }
}
