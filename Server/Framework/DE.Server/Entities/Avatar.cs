using DE.Server.NativeBridge;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public enum AvatarClientDetachReason
    {
        Disconnected = 1,
        ReplacedByNewLogin = 2,
    }

    public partial class AvatarEntity : ServerEntity
    {
        public AvatarEntity()
        {
        }

        public AvatarEntity(object entityDocument) : base(entityDocument)
        {
        }

        public override bool IsMigratable()
        {
            return true;
        }

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
            DELogger.Info(nameof(AvatarEntity), $"Avatar login registration finished, avatarId={Guid}.");
        }

        [ServerRpc]
        public virtual void OnAvatarClientAttached(ulong clientSessionId)
        {
            DELogger.Info(nameof(AvatarEntity), $"Avatar client attached, avatarId={Guid}, clientSessionId={clientSessionId}.");
        }

        [ServerRpc]
        public virtual void OnAvatarClientDetached(ulong clientSessionId, AvatarClientDetachReason reason)
        {
            DELogger.Info(nameof(AvatarEntity), $"Avatar client detached, avatarId={Guid}, clientSessionId={clientSessionId}, reason={reason}.");
        }
    }
}
