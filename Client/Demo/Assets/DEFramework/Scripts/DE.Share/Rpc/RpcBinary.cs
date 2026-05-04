using System;
using System.Text;

namespace DE.Share.Rpc
{
    public sealed class RpcBinaryWriter
    {
        private byte[] _buffer = new byte[64];
        private int _count;

        public void WriteString(string value)
        {
            var bytes = string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(value);
            WriteInt32(bytes.Length);
            WriteBytes(bytes);
        }

        public void WriteInt32(int value)
        {
            EnsureCapacity(4);
            _buffer[_count++] = (byte)((value >> 24) & 0xff);
            _buffer[_count++] = (byte)((value >> 16) & 0xff);
            _buffer[_count++] = (byte)((value >> 8) & 0xff);
            _buffer[_count++] = (byte)(value & 0xff);
        }

        public byte[] ToArray()
        {
            var bytes = new byte[_count];
            Buffer.BlockCopy(_buffer, 0, bytes, 0, _count);
            return bytes;
        }

        private void WriteBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return;
            }

            EnsureCapacity(bytes.Length);
            Buffer.BlockCopy(bytes, 0, _buffer, _count, bytes.Length);
            _count += bytes.Length;
        }

        private void EnsureCapacity(int appendSize)
        {
            var requiredSize = _count + appendSize;
            if (_buffer.Length >= requiredSize)
            {
                return;
            }

            var newSize = _buffer.Length;
            while (newSize < requiredSize)
            {
                newSize *= 2;
            }

            Array.Resize(ref _buffer, newSize);
        }
    }

    public sealed class RpcBinaryReader
    {
        private readonly byte[] _buffer;
        private int _offset;

        public RpcBinaryReader(byte[] buffer)
        {
            _buffer = buffer ?? Array.Empty<byte>();
        }

        public string ReadString()
        {
            var length = ReadInt32();
            if (length < 0 || _buffer.Length - _offset < length)
            {
                throw new InvalidOperationException("Invalid RPC string payload.");
            }

            if (length == 0)
            {
                return string.Empty;
            }

            var value = Encoding.UTF8.GetString(_buffer, _offset, length);
            _offset += length;
            return value;
        }

        public int ReadInt32()
        {
            if (_buffer.Length - _offset < 4)
            {
                throw new InvalidOperationException("Invalid RPC int payload.");
            }

            var value = (_buffer[_offset] << 24)
                | (_buffer[_offset + 1] << 16)
                | (_buffer[_offset + 2] << 8)
                | _buffer[_offset + 3];
            _offset += 4;
            return value;
        }
    }
}
