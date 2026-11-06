using DE.Server.NativeBridge;
using DE.Share.Entities;
using DE.Share.Foundation.Mathematics;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public enum AvatarClientDetachReason
    {
        Disconnected = 1,
        ReplacedByNewLogin = 2,
    }

    public partial class AvatarEntity : ServerEntity, ISpatialEntity
    {
        public AvatarEntity()
        {
            Space = AddComponent(new SpaceComponent());
        }

        public AvatarEntity(object entityDocument) : base(entityDocument)
        {
            Space = AddComponent(new SpaceComponent());
        }

        protected override void PreInitialize()
        {
            base.PreInitialize();
            CurrentSpace = null;
            ImmigrationState = AvatarImmigrationState.Idle;
            SpaceId = System.Guid.Empty;
            SpaceNo = 0;
            Position = default;
            Rotation = Quaternion.Identity;
        }

        public SpaceComponent Space { get; }
        
        public EntityProxy Proxy { get; private set; }

        internal void AttachToGateServer(string bindingGate)
        {
            Proxy = new EntityProxy(Guid, bindingGate);
        }

        public bool CallClient(string methodName, params object[] args)
        {
            uint methodId = RpcMethodId.Compute(methodName, args);
            byte[] argsPayload = RpcBinaryWriter.SerializeArguments(args);
            return AvatarRpcCaller.CallClient(this, methodId, argsPayload);
        }

        [ServerRpc]
        public virtual void OnAvatarLoginFinish()
        {
#if DEBUG
            DELogger.Info(nameof(AvatarEntity), $"Avatar login registration finished, avatarId={Guid}.");
#endif
        }

        [ServerRpc]
        public virtual void OnAvatarClientAttached(ulong clientSessionId)
        {
#if DEBUG
            DELogger.Info(nameof(AvatarEntity), $"Avatar client attached, avatarId={Guid}, clientSessionId={clientSessionId}.");
#endif
        }

        [ServerRpc]
        public virtual void OnAvatarClientDetached(ulong clientSessionId, AvatarClientDetachReason reason)
        {
#if DEBUG
            DELogger.Info(nameof(AvatarEntity), $"Avatar client detached, avatarId={Guid}, clientSessionId={clientSessionId}, reason={reason}.");
#endif
        }
    }

    public class AvatarComponent : EntityComponent
    {
        private AvatarEntity _ownerAvatar;
        public AvatarEntity OwnerAvatar => _ownerAvatar;

        protected override void PreInitialize()
        {
            base.PreInitialize();
            _ownerAvatar = (AvatarEntity)OwnerEntity;
        }
    }
}
