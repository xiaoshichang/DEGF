using DE.Share.Entities;

namespace Assets.Scripts.Demo.Client.Entities
{
    public partial class DemoAvatarEntity : AvatarEntity
    {
        [EntityProperty(EntityPropertyFlag.ClientServer)]
        private string __HeadIcon = "";

        [EntityProperty(EntityPropertyFlag.ClientServer)]
        private int __Score = 0;
    }
}
