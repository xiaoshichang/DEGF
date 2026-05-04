using DE.Server.Entities;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace Demo;

public partial class DemoAvatarEntity : AvatarEntity
{
    public DemoAvatarEntity()
    {
        BasicInfo = AddComponent(new BasicInfoComponent());
    }

    public BasicInfoComponent BasicInfo { get; }

    [ServerRpc]
    public void SetHeadIcon(string headIcon)
    {
        BasicInfo.HeadIcon = headIcon ?? string.Empty;
        CallClient("NotifyHeadIconChanged", BasicInfo.HeadIcon);
    }
}
