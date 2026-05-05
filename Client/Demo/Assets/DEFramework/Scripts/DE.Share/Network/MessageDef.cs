namespace Assets.Scripts.DE.Share
{
    using System;
    using System.Text;

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
                LoginReq = 0x00010004u,
                LoginRsp = 0x00010005u,
                RpcNtf = 0x00010006u,
            }

            public enum SS : uint
            {
                HandShakeReq = 0x00020001u,
                HandShakeRsp = 0x00020002u,
                HeartBeatWithDataNtf = 0x00020003u,
                AllNodeReadyNtf = 0x00020004u,
                StubDistributeNtf = 0x00020009u,
                GameReadyNtf = 0x00020005u,
                OpenGateNtf = 0x00020006u,
                CreateAvatarReq = 0x00020007u,
                CreateAvatarRsp = 0x00020008u,
                AvatarRpcNtf = 0x0002000Au,
                ServerRpcNtf = 0x0002000Bu,
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

        public struct LoginReq
        {
            public const ushort CurrentVersion = 1;
            public const int FixedWireSize = 6;

            public ushort Version;
            public ushort Reserved;
            public string Account;

            public byte[] Serialize()
            {
                byte[] accountBytes = string.IsNullOrEmpty(Account) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(Account);
                if (accountBytes.Length > ushort.MaxValue)
                {
                    throw new InvalidOperationException("LoginReq account is too long.");
                }

                byte[] bytes = new byte[FixedWireSize + accountBytes.Length];
                WriteUInt16BigEndian(bytes, 0, Version);
                WriteUInt16BigEndian(bytes, 2, Reserved);
                WriteUInt16BigEndian(bytes, 4, (ushort)accountBytes.Length);
                if (accountBytes.Length > 0)
                {
                    Buffer.BlockCopy(accountBytes, 0, bytes, FixedWireSize, accountBytes.Length);
                }

                return bytes;
            }

            public static bool TryDeserialize(byte[] data, int offset, int dataSize, out LoginReq message)
            {
                message = default(LoginReq);
                if (data == null || offset < 0 || dataSize < FixedWireSize || data.Length - offset < dataSize)
                {
                    return false;
                }

                LoginReq parsed = default(LoginReq);
                parsed.Version = ReadUInt16BigEndian(data, offset);
                parsed.Reserved = ReadUInt16BigEndian(data, offset + 2);
                int accountLength = ReadUInt16BigEndian(data, offset + 4);
                if (parsed.Version != CurrentVersion || dataSize != FixedWireSize + accountLength)
                {
                    return false;
                }

                parsed.Account = accountLength == 0
                    ? string.Empty
                    : Encoding.UTF8.GetString(data, offset + FixedWireSize, accountLength);
                message = parsed;
                return true;
            }
        }

        public struct LoginRsp
        {
            public const ushort CurrentVersion = 1;
            public const int FixedWireSize = 30;

            public ushort Version;
            public bool IsSuccess;
            public byte Reserved;
            public int StatusCode;
            public Guid AvatarId;
            public byte[] AvatarData;
            public string Error;

            public byte[] Serialize()
            {
                byte[] errorBytes = string.IsNullOrEmpty(Error) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(Error);
                byte[] avatarDataBytes = AvatarData ?? Array.Empty<byte>();
                if (errorBytes.Length > ushort.MaxValue)
                {
                    throw new InvalidOperationException("LoginRsp error is too long.");
                }

                byte[] bytes = new byte[FixedWireSize + errorBytes.Length + avatarDataBytes.Length];
                WriteUInt16BigEndian(bytes, 0, Version);
                bytes[2] = IsSuccess ? (byte)1 : (byte)0;
                bytes[3] = Reserved;
                WriteUInt32BigEndian(bytes, 4, unchecked((uint)StatusCode));
                byte[] avatarBytes = AvatarId.ToByteArray();
                Buffer.BlockCopy(avatarBytes, 0, bytes, 8, avatarBytes.Length);
                WriteUInt16BigEndian(bytes, 24, (ushort)errorBytes.Length);
                WriteUInt32BigEndian(bytes, 26, (uint)avatarDataBytes.Length);
                if (errorBytes.Length > 0)
                {
                    Buffer.BlockCopy(errorBytes, 0, bytes, FixedWireSize, errorBytes.Length);
                }

                if (avatarDataBytes.Length > 0)
                {
                    Buffer.BlockCopy(avatarDataBytes, 0, bytes, FixedWireSize + errorBytes.Length, avatarDataBytes.Length);
                }

                return bytes;
            }

            public static bool TryDeserialize(byte[] data, int offset, int dataSize, out LoginRsp message)
            {
                message = default(LoginRsp);
                if (data == null || offset < 0 || dataSize < FixedWireSize || data.Length - offset < dataSize)
                {
                    return false;
                }

                LoginRsp parsed = default(LoginRsp);
                parsed.Version = ReadUInt16BigEndian(data, offset);
                parsed.IsSuccess = data[offset + 2] != 0;
                parsed.Reserved = data[offset + 3];
                parsed.StatusCode = unchecked((int)ReadUInt32BigEndian(data, offset + 4));
                byte[] avatarBytes = new byte[16];
                Buffer.BlockCopy(data, offset + 8, avatarBytes, 0, avatarBytes.Length);
                parsed.AvatarId = new Guid(avatarBytes);
                int errorLength = ReadUInt16BigEndian(data, offset + 24);
                uint avatarDataLength = ReadUInt32BigEndian(data, offset + 26);
                if (parsed.Version != CurrentVersion || avatarDataLength > int.MaxValue || dataSize != FixedWireSize + errorLength + (int)avatarDataLength)
                {
                    return false;
                }

                parsed.Error = errorLength == 0
                    ? string.Empty
                    : Encoding.UTF8.GetString(data, offset + FixedWireSize, errorLength);
                parsed.AvatarData = avatarDataLength == 0
                    ? Array.Empty<byte>()
                    : new byte[(int)avatarDataLength];
                if (avatarDataLength > 0)
                {
                    Buffer.BlockCopy(data, offset + FixedWireSize + errorLength, parsed.AvatarData, 0, (int)avatarDataLength);
                }

                message = parsed;
                return true;
            }
        }

        public struct CreateAvatarReq
        {
            public const ushort CurrentVersion = 1;
            public const int WireSize = 20;

            public ushort Version;
            public ushort Reserved;
            public Guid AvatarId;

            public byte[] Serialize()
            {
                byte[] bytes = new byte[WireSize];
                WriteUInt16BigEndian(bytes, 0, Version);
                WriteUInt16BigEndian(bytes, 2, Reserved);
                byte[] avatarBytes = AvatarId.ToByteArray();
                Buffer.BlockCopy(avatarBytes, 0, bytes, 4, avatarBytes.Length);
                return bytes;
            }

            public static bool TryDeserialize(byte[] data, int offset, int dataSize, out CreateAvatarReq message)
            {
                message = default(CreateAvatarReq);
                if (data == null || offset < 0 || dataSize != WireSize || data.Length - offset < WireSize)
                {
                    return false;
                }

                CreateAvatarReq parsed = default(CreateAvatarReq);
                parsed.Version = ReadUInt16BigEndian(data, offset);
                parsed.Reserved = ReadUInt16BigEndian(data, offset + 2);
                byte[] avatarBytes = new byte[16];
                Buffer.BlockCopy(data, offset + 4, avatarBytes, 0, avatarBytes.Length);
                parsed.AvatarId = new Guid(avatarBytes);
                if (parsed.Version != CurrentVersion || parsed.AvatarId == Guid.Empty)
                {
                    return false;
                }

                message = parsed;
                return true;
            }
        }

        public struct CreateAvatarRsp
        {
            public const ushort CurrentVersion = 1;
            public const int FixedWireSize = 30;

            public ushort Version;
            public bool IsSuccess;
            public byte Reserved;
            public int StatusCode;
            public Guid AvatarId;
            public byte[] AvatarData;
            public string Error;

            public byte[] Serialize()
            {
                byte[] errorBytes = string.IsNullOrEmpty(Error) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(Error);
                byte[] avatarDataBytes = AvatarData ?? Array.Empty<byte>();
                if (errorBytes.Length > ushort.MaxValue)
                {
                    throw new InvalidOperationException("CreateAvatarRsp error is too long.");
                }

                byte[] bytes = new byte[FixedWireSize + errorBytes.Length + avatarDataBytes.Length];
                WriteUInt16BigEndian(bytes, 0, Version);
                bytes[2] = IsSuccess ? (byte)1 : (byte)0;
                bytes[3] = Reserved;
                WriteUInt32BigEndian(bytes, 4, unchecked((uint)StatusCode));
                byte[] avatarBytes = AvatarId.ToByteArray();
                Buffer.BlockCopy(avatarBytes, 0, bytes, 8, avatarBytes.Length);
                WriteUInt16BigEndian(bytes, 24, (ushort)errorBytes.Length);
                WriteUInt32BigEndian(bytes, 26, (uint)avatarDataBytes.Length);
                if (errorBytes.Length > 0)
                {
                    Buffer.BlockCopy(errorBytes, 0, bytes, FixedWireSize, errorBytes.Length);
                }

                if (avatarDataBytes.Length > 0)
                {
                    Buffer.BlockCopy(avatarDataBytes, 0, bytes, FixedWireSize + errorBytes.Length, avatarDataBytes.Length);
                }

                return bytes;
            }

            public static bool TryDeserialize(byte[] data, int offset, int dataSize, out CreateAvatarRsp message)
            {
                message = default(CreateAvatarRsp);
                if (data == null || offset < 0 || dataSize < FixedWireSize || data.Length - offset < dataSize)
                {
                    return false;
                }

                CreateAvatarRsp parsed = default(CreateAvatarRsp);
                parsed.Version = ReadUInt16BigEndian(data, offset);
                parsed.IsSuccess = data[offset + 2] != 0;
                parsed.Reserved = data[offset + 3];
                parsed.StatusCode = unchecked((int)ReadUInt32BigEndian(data, offset + 4));
                byte[] avatarBytes = new byte[16];
                Buffer.BlockCopy(data, offset + 8, avatarBytes, 0, avatarBytes.Length);
                parsed.AvatarId = new Guid(avatarBytes);
                int errorLength = ReadUInt16BigEndian(data, offset + 24);
                uint avatarDataLength = ReadUInt32BigEndian(data, offset + 26);
                if (parsed.Version != CurrentVersion || avatarDataLength > int.MaxValue || dataSize != FixedWireSize + errorLength + (int)avatarDataLength)
                {
                    return false;
                }

                parsed.Error = errorLength == 0
                    ? string.Empty
                    : Encoding.UTF8.GetString(data, offset + FixedWireSize, errorLength);
                parsed.AvatarData = avatarDataLength == 0
                    ? Array.Empty<byte>()
                    : new byte[(int)avatarDataLength];
                if (avatarDataLength > 0)
                {
                    Buffer.BlockCopy(data, offset + FixedWireSize + errorLength, parsed.AvatarData, 0, (int)avatarDataLength);
                }

                message = parsed;
                return true;
            }
        }

        public struct AvatarRpc
        {
            public const ushort CurrentVersion = 1;
            public const int FixedWireSize = 28;

            public ushort Version;
            public ushort Reserved;
            public Guid AvatarId;
            public uint MethodId;
            public byte[] ArgsPayload;

            public byte[] Serialize()
            {
                byte[] argsPayloadBytes = ArgsPayload ?? Array.Empty<byte>();

                byte[] bytes = new byte[FixedWireSize + argsPayloadBytes.Length];
                WriteUInt16BigEndian(bytes, 0, Version);
                WriteUInt16BigEndian(bytes, 2, Reserved);
                byte[] avatarBytes = AvatarId.ToByteArray();
                Buffer.BlockCopy(avatarBytes, 0, bytes, 4, avatarBytes.Length);
                WriteUInt32BigEndian(bytes, 20, MethodId);
                WriteUInt32BigEndian(bytes, 24, (uint)argsPayloadBytes.Length);
                if (argsPayloadBytes.Length > 0)
                {
                    Buffer.BlockCopy(argsPayloadBytes, 0, bytes, FixedWireSize, argsPayloadBytes.Length);
                }

                return bytes;
            }

            public static bool TryDeserialize(byte[] data, int offset, int dataSize, out AvatarRpc message)
            {
                message = default(AvatarRpc);
                if (data == null || offset < 0 || dataSize < FixedWireSize || data.Length - offset < dataSize)
                {
                    return false;
                }

                AvatarRpc parsed = default(AvatarRpc);
                parsed.Version = ReadUInt16BigEndian(data, offset);
                parsed.Reserved = ReadUInt16BigEndian(data, offset + 2);
                byte[] avatarBytes = new byte[16];
                Buffer.BlockCopy(data, offset + 4, avatarBytes, 0, avatarBytes.Length);
                parsed.AvatarId = new Guid(avatarBytes);
                parsed.MethodId = ReadUInt32BigEndian(data, offset + 20);
                uint argsPayloadLength = ReadUInt32BigEndian(data, offset + 24);
                if (parsed.Version != CurrentVersion || argsPayloadLength > int.MaxValue || dataSize != FixedWireSize + (int)argsPayloadLength)
                {
                    return false;
                }

                parsed.ArgsPayload = argsPayloadLength == 0
                    ? Array.Empty<byte>()
                    : new byte[(int)argsPayloadLength];
                if (argsPayloadLength > 0)
                {
                    Buffer.BlockCopy(data, offset + FixedWireSize, parsed.ArgsPayload, 0, (int)argsPayloadLength);
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
