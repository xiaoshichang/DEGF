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

    public partial class ServerAvatarEntity : ServerEntity, ISpatialEntity
    {
        public ServerAvatarEntity()
        {
            Space = AddComponent(new ServerSpaceComponent());
            AOIAgent = AddComponent(new AOIAgentComponent());
        }

        public ServerAvatarEntity(object entityDocument) : base(entityDocument)
        {
            Space = AddComponent(new ServerSpaceComponent());
            AOIAgent = AddComponent(new AOIAgentComponent());
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

        protected override void UnInitialize()
        {
            CancelSpaceRequest();
            CurrentSpace = null;
            base.UnInitialize();
        }

        public ServerSpaceComponent Space { get; }
        public AOIAgentComponent AOIAgent { get; }
        
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
            DELogger.Info(nameof(ServerAvatarEntity), $"Avatar login registration finished, avatarId={Guid}.");
#endif
        }

        [ServerRpc]
        public virtual void OnAvatarClientAttached(ulong clientSessionId)
        {
#if DEBUG
            DELogger.Info(nameof(ServerAvatarEntity), $"Avatar client attached, avatarId={Guid}, clientSessionId={clientSessionId}.");
#endif
            EnterLoginSpace();
        }

        [ServerRpc]
        public virtual void OnAvatarClientDetached(ulong clientSessionId, AvatarClientDetachReason reason)
        {
#if DEBUG
            DELogger.Info(nameof(ServerAvatarEntity), $"Avatar client detached, avatarId={Guid}, clientSessionId={clientSessionId}, reason={reason}.");
#endif
        }
    }
}
