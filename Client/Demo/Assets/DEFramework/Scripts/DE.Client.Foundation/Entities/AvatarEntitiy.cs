using Assets.Scripts.DE.Client.Framework;
using DE.Client.Entities;
using DE.Share.Entities;
using DE.Share.Foundation.Mathematics;
using DE.Share.Rpc;

public partial class AvatarEntity : ClientEntity, ISpatialEntity
{
    public AvatarEntity()
    {
        Space = AddComponent(new SpaceComponent());
    }

    public AvatarEntity(object entityDocument) : base(entityDocument)
    {
        Space = AddComponent(new SpaceComponent());
    }

    public SpaceComponent Space { get; }
    private ClientSpaceManager _spaces;
    public ClientSpaceManager Spaces => _spaces;

    protected override void PreInitialize()
    {
        base.PreInitialize();
        SpaceId = System.Guid.Empty;
        SpaceNo = 0;
        Position = default;
        Rotation = Quaternion.Identity;
    }

    protected override void Initialize()
    {
        _spaces = new ClientSpaceManager(this);
        base.Initialize();
    }

    [ClientRpc]
    public void NotifySpaceEntered(byte[] snapshot)
    {
        try
        {
            Spaces.BeginLoading(SpaceSnapshotCodec.Deserialize(snapshot));
        }
        catch (System.Exception error)
        {
            Spaces.Fail(error.Message);
        }
    }

    [ClientRpc]
    public void NotifySpaceEntityAppeared(System.Guid spaceId, byte[] snapshot)
    {
        Spaces.EntityAppeared(spaceId, SpaceSnapshotCodec.DeserializeEntity(snapshot));
    }

    [ClientRpc]
    public void NotifySpaceEntityDisappeared(System.Guid spaceId, System.Guid entityId)
    {
        Spaces.EntityDisappeared(spaceId, entityId);
    }

    [ClientRpc]
    public void NotifySpaceEnterFailed(string error)
    {
        Assets.Scripts.DE.Client.Core.DELogger.Error(error);
        Spaces.NotifyEntryFailed(error);
    }

    public override void OnRemovedFromSpace()
    {
        Spaces.Reset();
        base.OnRemovedFromSpace();
    }

    public bool CallServer(string methodName, params object[] args)
    {
        uint methodId = RpcMethodId.Compute(methodName, args);
        byte[] argsPayload = RpcBinaryWriter.SerializeArguments(args);
        return AuthSystem.Instance.SendAvatarServerRpc(methodId, argsPayload);
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
