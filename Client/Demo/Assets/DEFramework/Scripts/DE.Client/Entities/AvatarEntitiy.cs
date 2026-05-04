using Assets.Scripts.DE.Client.Framework;
using DE.Client.Entities;
using DE.Share.Rpc;

public class AvatarEntity : ClientEntity
{
    public AvatarEntity()
    {
    }

    public AvatarEntity(object entityDocument) : base(entityDocument)
    {
    }

    public bool CallServer(string methodName, params object[] args)
    {
        uint methodId = RpcMethodId.Compute(methodName, args);
        byte[] argsPayload = RpcBinaryWriter.SerializeArguments(args);
        return AuthSystem.Instance.SendAvatarServerRpc(methodId, argsPayload);
    }
}
