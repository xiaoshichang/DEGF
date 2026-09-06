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
}