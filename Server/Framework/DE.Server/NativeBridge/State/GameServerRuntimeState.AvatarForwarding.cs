using System;
using System.Linq;
using DE.Server.Entities;

namespace DE.Server.NativeBridge
{
    public sealed partial class GameServerRuntimeState
    {
        // KBE Cellapp::onEntityCall and GhostManager::getRoute forward instead of invoking Ghost scripts.
        private bool TryForwardMigratingAvatarRpc(ServerRpcPayload rpc, out bool sent)
        {
            sent = false;
            if (rpc.TargetKind != RpcTargetKind.ServerAvatarProxy && rpc.TargetKind != RpcTargetKind.ServerMailbox)
            {
                return false;
            }
            if (Avatars.TryGetValue(rpc.EntityId, out var avatar))
            {
                if (avatar.ImmigateCmpt.ImmigrationState != AvatarImmigrationState.Ghost)
                {
                    return false;
                }
            }
            if (!_avatarForwardingRoutes.TryGetValue(rpc.EntityId, out var route))
            {
                return false;
            }
            if (route.ExpiresAtUtc <= DateTime.UtcNow)
            {
                _avatarForwardingRoutes.Remove(rpc.EntityId);
                return false;
            }
            var forwarded = new ServerRpcPayload
            {
                Version = ServerRpcPayload.CurrentVersion,
                TargetKind = RpcTargetKind.ServerMailbox,
                EntityId = rpc.EntityId,
                TargetServerId = route.Message.TargetGameServerId,
                MethodId = rpc.MethodId,
                ArgsPayload = rpc.ArgsPayload,
            };
            // The specific target address prevents the OwnerGate from routing this back to the source Ghost.
            sent = NativeAPI.SendServerRpcToServer(route.Message.GateServerId, forwarded.Serialize());
            return true;
        }

        // KBE GhostManager::checkRoute expires old addresses after five seconds; sweep without a new timer.
        private void RemoveExpiredAvatarForwardingRoutes()
        {
            var now = DateTime.UtcNow;
            foreach (var id in _avatarForwardingRoutes.Where(pair => pair.Value.ExpiresAtUtc <= now)
                .Select(pair => pair.Key).ToArray())
            {
                _avatarForwardingRoutes.Remove(id);
            }
        }
    }
}
