using System;
using DE.Server.NativeBridge;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
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

        public bool CallClient(string methodName, params object[] args)
        {
            uint methodId = RpcMethodId.Compute(methodName, args);
            byte[] argsPayload = RpcBinaryWriter.SerializeArguments(args);
            return AvatarRpcRuntime.SendClientAvatarRpc(this, methodId, argsPayload);
        }
    }
}
