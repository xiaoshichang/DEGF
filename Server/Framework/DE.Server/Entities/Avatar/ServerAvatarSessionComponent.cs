using DE.Server.NativeBridge;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public enum AvatarClientDetachReason
    {
        Disconnected = 1,
        ReplacedByNewLogin = 2,
    }

    public partial class ServerAvatarSessionComponent : ServerEntityComponent
    {
        private ServerAvatarEntity Avatar => (ServerAvatarEntity)OwnerEntity;

        [ServerRpc]
        public virtual void OnAvatarLoginFinish()
        {
#if DEBUG
            DELogger.Info(nameof(ServerAvatarEntity), $"Avatar login registration finished, avatarId={Avatar.Guid}.");
#endif
        }

        [ServerRpc]
        public virtual void OnAvatarClientAttached(ulong clientSessionId)
        {
#if DEBUG
            DELogger.Info(nameof(ServerAvatarEntity), $"Avatar client attached, avatarId={Avatar.Guid}, clientSessionId={clientSessionId}.");
#endif
            Avatar.SpaceCmpt.EnterLoginSpace();
        }

        [ServerRpc]
        public virtual void OnAvatarClientDetached(ulong clientSessionId, AvatarClientDetachReason reason)
        {
            ManagedRuntimeState.RequireCurrentGameServerRuntimeState().DetachAvatarClientSession(Avatar, clientSessionId);
#if DEBUG
            DELogger.Info(nameof(ServerAvatarEntity), $"Avatar client detached, avatarId={Avatar.Guid}, clientSessionId={clientSessionId}, reason={reason}.");
#endif
        }
    }
}
