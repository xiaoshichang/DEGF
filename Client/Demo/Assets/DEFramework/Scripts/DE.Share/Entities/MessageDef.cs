namespace Assets.Scripts.DE.Share
{
    public static class MessageDef
    {
        public static class MessageID
        {
            public const uint CategoryMask = 0xffff0000u;
            public const uint CSCategory = 0x00010000u;
            public const uint SSCategory = 0x00020000u;

            public enum CS : uint
            {
                HandShakeReq = 0x00010001u,
                HandShakeRsp = 0x00010002u,
                HeartBeatNtf = 0x00010003u,
            }

            public enum SS : uint
            {
                HandShakeReq = 0x00020001u,
                HandShakeRsp = 0x00020002u,
                HeartBeatWithDataNtf = 0x00020003u,
                AllNodeReadyNtf = 0x00020004u,
                GameReadyNtf = 0x00020005u,
                OpenGateNtf = 0x00020006u,
            }

            public static bool IsCS(uint messageId)
            {
                return (messageId & CategoryMask) == CSCategory;
            }

            public static bool IsSS(uint messageId)
            {
                return (messageId & CategoryMask) == SSCategory;
            }
        }

        public struct Header
        {
            public const ushort CurrentVersion = 1;
            public const ushort WireSize = 24;
            public const uint DefaultFlags = 0;
            public const uint Magic = 0x44454E47;

            public uint MagicValue;
            public ushort Version;
            public ushort HeaderLength;
            public uint Length;
            public uint MessageId;
            public uint Flags;
            public uint Reserved;

            public static Header CreateClient(uint messageId, uint payloadLength, uint flags = DefaultFlags)
            {
                Header header = default(Header);
                header.MagicValue = Magic;
                header.Version = CurrentVersion;
                header.HeaderLength = WireSize;
                header.Length = payloadLength;
                header.MessageId = messageId;
                header.Flags = flags;
                header.Reserved = 0;
                return header;
            }

            public bool HasValidMagic()
            {
                return MagicValue == Magic;
            }

            public bool HasValidLayout()
            {
                return Version == CurrentVersion && HeaderLength == WireSize;
            }

            public bool IsValid()
            {
                return HasValidMagic() && HasValidLayout();
            }

            public uint GetFrameLength()
            {
                return HeaderLength + Length;
            }

            public byte[] Serialize()
            {
                byte[] bytes = new byte[WireSize];
                WriteUInt32BigEndian(bytes, 0, MagicValue);
                WriteUInt16BigEndian(bytes, 4, Version);
                WriteUInt16BigEndian(bytes, 6, HeaderLength);
                WriteUInt32BigEndian(bytes, 8, Length);
                WriteUInt32BigEndian(bytes, 12, MessageId);
                WriteUInt32BigEndian(bytes, 16, Flags);
                WriteUInt32BigEndian(bytes, 20, Reserved);
                return bytes;
            }

            public static bool TryDeserialize(byte[] data, int offset, int dataSize, out Header header)
            {
                header = default(Header);
                if (data == null || offset < 0 || dataSize < WireSize || data.Length - offset < WireSize)
                {
                    return false;
                }

                Header parsed = default(Header);
                parsed.MagicValue = ReadUInt32BigEndian(data, offset);
                parsed.Version = ReadUInt16BigEndian(data, offset + 4);
                parsed.HeaderLength = ReadUInt16BigEndian(data, offset + 6);
                parsed.Length = ReadUInt32BigEndian(data, offset + 8);
                parsed.MessageId = ReadUInt32BigEndian(data, offset + 12);
                parsed.Flags = ReadUInt32BigEndian(data, offset + 16);
                parsed.Reserved = ReadUInt32BigEndian(data, offset + 20);
                if (!parsed.IsValid())
                {
                    return false;
                }

                header = parsed;
                return true;
            }
        }

        public struct ClientHandShakeMessage
        {
            public const ushort CurrentVersion = 1;
            public const int WireSize = 12;

            public ushort Version;
            public ushort Reserved;
            public ulong SessionId;

            public byte[] Serialize()
            {
                byte[] bytes = new byte[WireSize];
                WriteUInt16BigEndian(bytes, 0, Version);
                WriteUInt16BigEndian(bytes, 2, Reserved);
                WriteUInt64BigEndian(bytes, 4, SessionId);
                return bytes;
            }

            public static bool TryDeserialize(byte[] data, int offset, int dataSize, out ClientHandShakeMessage message)
            {
                message = default(ClientHandShakeMessage);
                if (data == null || offset < 0 || dataSize != WireSize || data.Length - offset < WireSize)
                {
                    return false;
                }

                ClientHandShakeMessage parsed = default(ClientHandShakeMessage);
                parsed.Version = ReadUInt16BigEndian(data, offset);
                parsed.Reserved = ReadUInt16BigEndian(data, offset + 2);
                parsed.SessionId = ReadUInt64BigEndian(data, offset + 4);
                if (parsed.Version != CurrentVersion || parsed.SessionId == 0)
                {
                    return false;
                }

                message = parsed;
                return true;
            }
        }

        private static void WriteUInt16BigEndian(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)((value >> 8) & 0xff);
            buffer[offset + 1] = (byte)(value & 0xff);
        }

        private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xff);
            buffer[offset + 1] = (byte)((value >> 16) & 0xff);
            buffer[offset + 2] = (byte)((value >> 8) & 0xff);
            buffer[offset + 3] = (byte)(value & 0xff);
        }

        private static void WriteUInt64BigEndian(byte[] buffer, int offset, ulong value)
        {
            buffer[offset] = (byte)((value >> 56) & 0xff);
            buffer[offset + 1] = (byte)((value >> 48) & 0xff);
            buffer[offset + 2] = (byte)((value >> 40) & 0xff);
            buffer[offset + 3] = (byte)((value >> 32) & 0xff);
            buffer[offset + 4] = (byte)((value >> 24) & 0xff);
            buffer[offset + 5] = (byte)((value >> 16) & 0xff);
            buffer[offset + 6] = (byte)((value >> 8) & 0xff);
            buffer[offset + 7] = (byte)(value & 0xff);
        }

        private static ushort ReadUInt16BigEndian(byte[] buffer, int offset)
        {
            return (ushort)(((ushort)buffer[offset] << 8) | buffer[offset + 1]);
        }

        private static uint ReadUInt32BigEndian(byte[] buffer, int offset)
        {
            return ((uint)buffer[offset] << 24)
                | ((uint)buffer[offset + 1] << 16)
                | ((uint)buffer[offset + 2] << 8)
                | buffer[offset + 3];
        }

        private static ulong ReadUInt64BigEndian(byte[] buffer, int offset)
        {
            return ((ulong)buffer[offset] << 56)
                | ((ulong)buffer[offset + 1] << 48)
                | ((ulong)buffer[offset + 2] << 40)
                | ((ulong)buffer[offset + 3] << 32)
                | ((ulong)buffer[offset + 4] << 24)
                | ((ulong)buffer[offset + 5] << 16)
                | ((ulong)buffer[offset + 6] << 8)
                | buffer[offset + 7];
        }
    }
}
