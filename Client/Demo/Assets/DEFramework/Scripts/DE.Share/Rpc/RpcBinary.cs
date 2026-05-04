using System;
using System.Text;

namespace DE.Share.Rpc
{
    public sealed class RpcBinaryWriter
    {
        private byte[] _buffer = new byte[64];
        private int _count;

        public static byte[] SerializeArguments(object[] args)
        {
            var writer = new RpcBinaryWriter();
            if (args == null)
            {
                return writer.ToArray();
            }

            foreach (object arg in args)
            {
                writer.WriteObject(arg);
            }

            return writer.ToArray();
        }

        public void WriteObject(object value)
        {
            if (value == null || value is string)
            {
                WriteString((string)value);
                return;
            }

            if (value is int)
            {
                WriteInt32((int)value);
                return;
            }

            if (value is EntityProxy)
            {
                WriteEntityProxy((EntityProxy)value);
                return;
            }

            throw new NotSupportedException("Unsupported RPC argument type: " + value.GetType().FullName);
        }

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

        public void WriteGuid(Guid value)
        {
            WriteBytes(value.ToByteArray());
        }

        public void WriteEntityProxy(EntityProxy value)
        {
            WriteGuid(value.EntityId);
            WriteString(value.ServerId ?? string.Empty);
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

        public Guid ReadGuid()
        {
            if (_buffer.Length - _offset < 16)
            {
                throw new InvalidOperationException("Invalid RPC guid payload.");
            }

            var bytes = new byte[16];
            Buffer.BlockCopy(_buffer, _offset, bytes, 0, bytes.Length);
            _offset += bytes.Length;
            return new Guid(bytes);
        }

        public EntityProxy ReadEntityProxy()
        {
            return new EntityProxy(ReadGuid(), ReadString());
        }
    }

    public readonly struct EntityProxy
    {
        public EntityProxy(Guid entityId, string serverId)
        {
            EntityId = entityId;
            ServerId = serverId ?? string.Empty;
        }

        public Guid EntityId { get; }
        public string ServerId { get; }
        public bool IsValid => EntityId != Guid.Empty && !string.IsNullOrWhiteSpace(ServerId);
    }
}
