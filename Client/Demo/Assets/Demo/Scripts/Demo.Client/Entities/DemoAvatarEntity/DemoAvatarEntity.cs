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

        [ClientRpc]
        public void NotifyHeadIconChanged(string headIcon)
        {
            BasicInfo.HeadIcon = headIcon ?? string.Empty;
        }
    }
}
