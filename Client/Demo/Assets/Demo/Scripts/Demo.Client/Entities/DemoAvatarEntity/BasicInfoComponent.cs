using DE.Client.Entities;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace Assets.Scripts.Demo.Client.Entities
{
    public partial class BasicInfoComponent : ClientEntityComponent
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
