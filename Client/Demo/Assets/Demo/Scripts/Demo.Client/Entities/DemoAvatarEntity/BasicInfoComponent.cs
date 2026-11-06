using DE.Share.Entities;
using DE.Share.Rpc;

namespace Assets.Scripts.Demo.Client.Entities
{
    public partial class BasicInfoComponent : AvatarComponent
    {
        private DemoAvatarEntity _owner;

        protected override void PreInitialize()
        {
            base.PreInitialize();
            _owner = (DemoAvatarEntity)OwnerEntity;
        }

        [ClientRpc]
        public void NotifyHeadIconChanged(string headIcon)
        {
            _owner.HeadIcon = headIcon ?? string.Empty;
        }
    }
}
