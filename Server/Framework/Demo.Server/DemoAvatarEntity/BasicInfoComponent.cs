using DE.Server.Entities;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace Demo;


public partial class BasicInfoComponent : AvatarComponent
{
    [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.Persistent)]
    private string __HeadIcon = "xxx.png";

    [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.Persistent)]
    private int __Score = 1;

    [ServerRpc]
    public void SetHeadIcon(string headIcon)
    {
        HeadIcon = headIcon ?? string.Empty;
        OwnerAvatar.CallClient("NotifyHeadIconChanged", HeadIcon);
    }
}
