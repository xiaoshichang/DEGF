using System;
using System.Text;
using DE.Share.Foundation.Mathematics;

namespace DE.Client.Entities
{
    /// <summary>有界强类型读取接口；属性值通过独立切片限制读取范围，未知值可直接跳过。</summary>
    public struct EntityPropertyReader
    {
        private readonly byte[] _bytes;
        private readonly int _endOffset;
        private int _offset;

        public EntityPropertyReader(byte[] bytes, int offset, int length)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            if (offset < 0 || length < 0 || bytes.Length - offset < length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            _bytes = bytes;
            _offset = offset;
            _endOffset = offset + length;
        }

        public int Remaining => _endOffset - _offset;
        public void SkipRemaining() => _offset = _endOffset;

        public bool TrySlice(uint length, out EntityPropertyReader reader)
        {
            reader = default;
            if (length > (uint)Remaining)
            {
                return false;
            }
            reader = new EntityPropertyReader(_bytes, _offset, (int)length);
            _offset += (int)length;
            return true;
        }

        public bool TryReadByte(out byte value)
        {
            value = 0;
            if (Remaining < sizeof(byte))
            {
                return false;
            }
            value = _bytes[_offset++];
            return true;
        }

        public bool TryReadUInt16(out ushort value)
        {
            value = 0;
            if (Remaining < sizeof(ushort))
            {
                return false;
            }
            value = (ushort)((_bytes[_offset] << 8) | _bytes[_offset + 1]);
            _offset += sizeof(ushort);
            return true;
        }

        public bool TryReadUInt32(out uint value)
        {
            value = 0;
            if (Remaining < sizeof(uint))
            {
                return false;
            }
            value = ((uint)_bytes[_offset] << 24) | ((uint)_bytes[_offset + 1] << 16)
                | ((uint)_bytes[_offset + 2] << 8) | _bytes[_offset + 3];
            _offset += sizeof(uint);
            return true;
        }

        public bool TryReadUInt64(out ulong value)
        {
            value = 0;
            if (Remaining < sizeof(ulong))
            {
                return false;
            }
            value = ((ulong)_bytes[_offset] << 56) | ((ulong)_bytes[_offset + 1] << 48)
                | ((ulong)_bytes[_offset + 2] << 40) | ((ulong)_bytes[_offset + 3] << 32)
                | ((ulong)_bytes[_offset + 4] << 24) | ((ulong)_bytes[_offset + 5] << 16)
                | ((ulong)_bytes[_offset + 6] << 8) | _bytes[_offset + 7];
            _offset += sizeof(ulong);
            return true;
        }

        public bool TryReadBoolean(out bool value)
        {
            value = false;
            if (!TryReadByte(out byte raw) || raw > 1)
            {
                return false;
            }
            value = raw != 0;
            return true;
        }

        public bool TryReadSByte(out sbyte value)
        {
            bool valid = TryReadByte(out byte raw);
            value = unchecked((sbyte)raw);
            return valid;
        }

        public bool TryReadInt16(out short value)
        {
            bool valid = TryReadUInt16(out ushort raw);
            value = unchecked((short)raw);
            return valid;
        }

        public bool TryReadInt32(out int value)
        {
            bool valid = TryReadUInt32(out uint raw);
            value = unchecked((int)raw);
            return valid;
        }

        public bool TryReadInt64(out long value)
        {
            bool valid = TryReadUInt64(out ulong raw);
            value = unchecked((long)raw);
            return valid;
        }

        public bool TryReadChar(out char value)
        {
            bool valid = TryReadUInt16(out ushort raw);
            value = (char)raw;
            return valid;
        }

        public bool TryReadSingle(out float value)
        {
            bool valid = TryReadUInt32(out uint raw);
            value = BitConverter.ToSingle(BitConverter.GetBytes(raw), 0);
            return valid;
        }

        public bool TryReadDouble(out double value)
        {
            bool valid = TryReadUInt64(out ulong raw);
            value = BitConverter.Int64BitsToDouble(unchecked((long)raw));
            return valid;
        }

        public bool TryReadString(out string value)
        {
            value = null;
            if (!TryReadByte(out byte marker) || marker > 1)
            {
                return false;
            }
            if (marker == 0)
            {
                return Remaining == 0;
            }
            if (Remaining >= EntitySerializationLimits.MaximumValueBytes)
            {
                return false;
            }
            value = Remaining == 0 ? string.Empty : Encoding.UTF8.GetString(_bytes, _offset, Remaining);
            _offset = _endOffset;
            return true;
        }

        public byte[] ToArray()
        {
            var value = new byte[Remaining];
            Buffer.BlockCopy(_bytes, _offset, value, 0, value.Length);
            return value;
        }

        public bool TryReadGuid(out Guid value)
        {
            value = default;
            if (!TryReadBytes(16, out var bytes))
            {
                return false;
            }
            value = new Guid(bytes);
            return true;
        }

        public bool TryReadVector3(out Vector3 value)
        {
            value = default;
            if (!TryReadSingle(out float x) || !TryReadSingle(out float y) || !TryReadSingle(out float z))
            {
                return false;
            }
            value = new Vector3(x, y, z);
            return true;
        }

        public bool TryReadQuaternion(out Quaternion value)
        {
            value = default;
            if (!TryReadSingle(out float x) || !TryReadSingle(out float y)
                || !TryReadSingle(out float z) || !TryReadSingle(out float w))
            {
                return false;
            }
            value = new Quaternion(x, y, z, w);
            return true;
        }

        private bool TryReadBytes(int length, out byte[] value)
        {
            value = null;
            if (Remaining < length)
            {
                return false;
            }
            value = new byte[length];
            Buffer.BlockCopy(_bytes, _offset, value, 0, length);
            _offset += length;
            return true;
        }
    }
}
