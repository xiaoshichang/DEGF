using System;
using Assets.Scripts.DE.Share;
using DE.Server.Entities;
using DE.Share.Rpc;

namespace DE.Server.NativeBridge
{
    public static class AvatarRpcRuntime
    {
        public static bool SendClientAvatarRpc(AvatarEntity avatar, uint methodId, byte[] argsPayload)
        {
            if (avatar == null)
            {
                return false;
            }

            return ManagedRuntimeState
                .RequireCurrentGameServerRuntimeState()
                .SendAvatarRpcToSourceGate(avatar.Guid, methodId, argsPayload);
        }

        public static byte[] BuildPayload(Guid avatarId, uint methodId, byte[] argsPayload)
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
    }
}
