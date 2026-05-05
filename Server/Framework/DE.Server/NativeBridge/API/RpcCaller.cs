using System;
using Assets.Scripts.DE.Share;
using DE.Server.Entities;
using DE.Share.Rpc;

namespace DE.Server.NativeBridge
{
    public enum ServerRpcTargetKind : ushort
    {
        Entity = 1,
        Stub = 2,
        AvatarProxy = 3,
    }

    public struct ServerRpcPayload
    {
        public const ushort CurrentVersion = 2;
        public const int FixedWireSize = 32;

        public ushort Version;
        public ServerRpcTargetKind TargetKind;
        public Guid EntityId;
        public string TargetServerId;
        public string StubName;
        public uint MethodId;
        public byte[] ArgsPayload;

        public byte[] Serialize()
        {
            byte[] targetServerIdBytes = string.IsNullOrEmpty(TargetServerId)
                ? Array.Empty<byte>()
                : System.Text.Encoding.UTF8.GetBytes(TargetServerId);
            byte[] stubNameBytes = string.IsNullOrEmpty(StubName)
                ? Array.Empty<byte>()
                : System.Text.Encoding.UTF8.GetBytes(StubName);
            byte[] argsPayloadBytes = ArgsPayload ?? Array.Empty<byte>();
            if (targetServerIdBytes.Length > ushort.MaxValue)
            {
                throw new InvalidOperationException("Server RPC target server id is too long.");
            }

            if (stubNameBytes.Length > ushort.MaxValue)
            {
                throw new InvalidOperationException("Server RPC stub name is too long.");
            }

            byte[] bytes = new byte[FixedWireSize + targetServerIdBytes.Length + stubNameBytes.Length + argsPayloadBytes.Length];
            WriteUInt16BigEndian(bytes, 0, Version);
            WriteUInt16BigEndian(bytes, 2, (ushort)TargetKind);
            Buffer.BlockCopy(EntityId.ToByteArray(), 0, bytes, 4, 16);
            WriteUInt16BigEndian(bytes, 20, (ushort)targetServerIdBytes.Length);
            WriteUInt16BigEndian(bytes, 22, (ushort)stubNameBytes.Length);
            WriteUInt32BigEndian(bytes, 24, MethodId);
            WriteUInt32BigEndian(bytes, 28, (uint)argsPayloadBytes.Length);
            if (targetServerIdBytes.Length > 0)
            {
                Buffer.BlockCopy(targetServerIdBytes, 0, bytes, FixedWireSize, targetServerIdBytes.Length);
            }

            if (stubNameBytes.Length > 0)
            {
                Buffer.BlockCopy(stubNameBytes, 0, bytes, FixedWireSize + targetServerIdBytes.Length, stubNameBytes.Length);
            }

            if (argsPayloadBytes.Length > 0)
            {
                Buffer.BlockCopy(argsPayloadBytes, 0, bytes, FixedWireSize + targetServerIdBytes.Length + stubNameBytes.Length, argsPayloadBytes.Length);
            }

            return bytes;
        }

        public static bool TryDeserialize(byte[] data, int offset, int dataSize, out ServerRpcPayload payload)
        {
            payload = default(ServerRpcPayload);
            if (data == null || offset < 0 || dataSize < FixedWireSize || data.Length - offset < dataSize)
            {
                return false;
            }

            ServerRpcPayload parsed = default(ServerRpcPayload);
            parsed.Version = ReadUInt16BigEndian(data, offset);
            parsed.TargetKind = (ServerRpcTargetKind)ReadUInt16BigEndian(data, offset + 2);
            byte[] entityIdBytes = new byte[16];
            Buffer.BlockCopy(data, offset + 4, entityIdBytes, 0, entityIdBytes.Length);
            parsed.EntityId = new Guid(entityIdBytes);
            int targetServerIdLength = ReadUInt16BigEndian(data, offset + 20);
            int stubNameLength = ReadUInt16BigEndian(data, offset + 22);
            parsed.MethodId = ReadUInt32BigEndian(data, offset + 24);
            uint argsPayloadLength = ReadUInt32BigEndian(data, offset + 28);
            if (parsed.Version != CurrentVersion
                || argsPayloadLength > int.MaxValue
                || dataSize != FixedWireSize + targetServerIdLength + stubNameLength + (int)argsPayloadLength)
            {
                return false;
            }

            parsed.TargetServerId = targetServerIdLength == 0
                ? string.Empty
                : System.Text.Encoding.UTF8.GetString(data, offset + FixedWireSize, targetServerIdLength);
            parsed.StubName = stubNameLength == 0
                ? string.Empty
                : System.Text.Encoding.UTF8.GetString(data, offset + FixedWireSize + targetServerIdLength, stubNameLength);
            parsed.ArgsPayload = argsPayloadLength == 0
                ? Array.Empty<byte>()
                : new byte[(int)argsPayloadLength];
            if (argsPayloadLength > 0)
            {
                Buffer.BlockCopy(data, offset + FixedWireSize + targetServerIdLength + stubNameLength, parsed.ArgsPayload, 0, (int)argsPayloadLength);
            }

            payload = parsed;
            return true;
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
    }

    public static class AvatarRpcCaller
    {
        public static bool CallClient(AvatarEntity avatar, uint methodId, byte[] argsPayload)
        {
            if (avatar == null)
            {
                return false;
            }

            if (!avatar.Proxy.IsValid)
            {
                DELogger.Warn(nameof(AvatarRpcCaller), $"Cannot send avatar RPC because gate proxy is invalid, avatarId={avatar.Guid}.");
                return false;
            }

            var payload = RpcCaller.BuildAvatarRpcPayload(avatar.Guid, methodId, argsPayload);
            return NativeAPI.SendAvatarRpcToServer(avatar.Proxy.BindingGate, payload);
        }

        public static bool CallAvatarProxy(EntityProxy proxy, string methodName, params object[] args)
        {
            if (!proxy.IsValid)
            {
                DELogger.Warn(nameof(AvatarRpcCaller), $"Invalid avatar proxy for method {methodName}.");
                return false;
            }

            var payload = RpcCaller.BuildServerRpcPayload(ServerRpcTargetKind.AvatarProxy, proxy.EntityId, string.Empty, string.Empty, methodName, args);
            return NativeAPI.SendServerRpcToServer(proxy.BindingGate, payload);
        }
    }

    public static class StubCaller
    {
        public static bool Call(string stubName, string methodName, params object[] args)
        {
            var runtimeState = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            var targetServerId = runtimeState.FindStubServerId(stubName);
            if (string.IsNullOrWhiteSpace(targetServerId))
            {
                DELogger.Warn(nameof(StubCaller), $"Stub target not found, stubName={stubName}.");
                return false;
            }

            var payload = RpcCaller.BuildServerRpcPayload(ServerRpcTargetKind.Stub, Guid.Empty, targetServerId, stubName, methodName, args);
            if (string.Equals(targetServerId, ManagedRuntimeState.RequireCurrent().ServerId, StringComparison.Ordinal))
            {
                return runtimeState.HandleServerRpc(targetServerId, payload);
            }

            var gateServerId = ManagedRuntimeState.RequireCurrent().SelectGateServerId(stubName);
            if (string.IsNullOrWhiteSpace(gateServerId))
            {
                DELogger.Warn(nameof(StubCaller), $"Gate relay not found for stub RPC, stubName={stubName}, targetServerId={targetServerId}.");
                return false;
            }

            return NativeAPI.SendServerRpcToServer(gateServerId, payload);
        }
    }

    public static class EntityCaller
    {
        public static bool Call(EntityMailBox mailbox, string methodName, params object[] args)
        {
            if (!mailbox.IsValid)
            {
                DELogger.Warn(nameof(EntityCaller), $"Invalid entity mailbox for method {methodName}.");
                return false;
            }

            var payload = RpcCaller.BuildServerRpcPayload(ServerRpcTargetKind.Entity, mailbox.EntityId, mailbox.BindingGame, string.Empty, methodName, args);
            var runtimeState = ManagedRuntimeState.RequireCurrent();
            if (runtimeState.ServerType == ManagedRuntimeServerType.Game
                && string.Equals(mailbox.BindingGame, runtimeState.ServerId, StringComparison.Ordinal))
            {
                return runtimeState.GameServerRuntimeState.HandleServerRpc(mailbox.BindingGame, payload);
            }

            if (runtimeState.ServerType == ManagedRuntimeServerType.Game)
            {
                var gateServerId = runtimeState.SelectGateServerId(mailbox.EntityId.ToString("N"));
                if (string.IsNullOrWhiteSpace(gateServerId))
                {
                    DELogger.Warn(nameof(EntityCaller), $"Gate relay not found for entity RPC, entityId={mailbox.EntityId}, targetServerId={mailbox.BindingGame}.");
                    return false;
                }

                return NativeAPI.SendServerRpcToServer(gateServerId, payload);
            }

            return NativeAPI.SendServerRpcToServer(mailbox.BindingGame, payload);
        }
    }

    public static class RpcCaller
    {
        public static byte[] BuildAvatarRpcPayload(Guid avatarId, uint methodId, byte[] argsPayload)
        {
            var rpc = new MessageDef.AvatarRpc
            {
                Version = MessageDef.AvatarRpc.CurrentVersion,
                Reserved = 0,
                AvatarId = avatarId,
                MethodId = methodId,
                ArgsPayload = argsPayload ?? Array.Empty<byte>(),
            };
            return rpc.Serialize();
        }

        public static byte[] BuildServerRpcPayload(ServerRpcTargetKind targetKind, Guid entityId, string targetServerId, string stubName, string methodName, object[] args)
        {
            var payload = new ServerRpcPayload
            {
                Version = ServerRpcPayload.CurrentVersion,
                TargetKind = targetKind,
                EntityId = entityId,
                TargetServerId = targetServerId ?? string.Empty,
                StubName = stubName ?? string.Empty,
                MethodId = RpcMethodId.Compute(methodName, args),
                ArgsPayload = RpcBinaryWriter.SerializeArguments(args),
            };
            return payload.Serialize();
        }
    }
}
