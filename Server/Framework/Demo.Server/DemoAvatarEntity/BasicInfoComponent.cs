using DE.Server.Entities;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace Demo;


public partial class BasicInfoComponent : AvatarComponent
{
    private DemoAvatarEntity _owner = null!;

    protected override void PreInitialize()
    {
        base.PreInitialize();
        _owner = (DemoAvatarEntity)OwnerEntity;
    }

    [ServerRpc]
    public void SetHeadIcon(string headIcon)
    {
        _owner.HeadIcon = headIcon ?? string.Empty;
        _owner.CallClient("NotifyHeadIconChanged", _owner.HeadIcon);
    }
}
