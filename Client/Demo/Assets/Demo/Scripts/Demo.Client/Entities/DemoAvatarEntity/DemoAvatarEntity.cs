using DE.Share.Entities;
using DE.Share.Rpc;

namespace Assets.Scripts.Demo.Client.Entities
{
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
        }

        [ClientRpc]
        public void NotifyHeadIconChanged(string headIcon)
        {
            BasicInfo.HeadIcon = headIcon ?? string.Empty;
        }
    }
}
