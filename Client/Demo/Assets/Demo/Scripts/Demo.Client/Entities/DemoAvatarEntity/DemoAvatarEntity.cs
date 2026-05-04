using DE.Share.Entities;

namespace Assets.Scripts.Demo.Client.Entities
{
    public partial class DemoAvatarEntity : AvatarEntity
    {
        public DemoAvatarEntity()
        {
            BasicInfo = AddComponent(new BasicInfoComponent());
        }

        public BasicInfoComponent BasicInfo { get; }
    }
}
