using DE.Server.NativeBridge;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
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

            var payload = new ServerRpcPayload
            {
                Version = ServerRpcPayload.CurrentVersion,
                TargetKind = RpcTargetKind.ClientAvatarProxy,
                EntityId = avatar.Guid,
                TargetServerId = avatar.Proxy.BindingGate,
                MethodId = methodId,
                ArgsPayload = argsPayload,
            };
            return NativeAPI.SendServerRpcToServer(avatar.Proxy.BindingGate, payload.Serialize());
        }

        public static bool CallAvatarProxy(EntityProxy proxy, string methodName, params object[] args)
        {
            if (!proxy.IsValid)
            {
                DELogger.Warn(nameof(AvatarRpcCaller), $"Invalid avatar proxy for method {methodName}.");
                return false;
            }

            var payload = RpcCaller.BuildServerRpcPayload(RpcTargetKind.ServerAvatarProxy, proxy.EntityId, string.Empty, string.Empty, methodName, args);
            return NativeAPI.SendServerRpcToServer(proxy.BindingGate, payload);
        }
    }
}