using DE.Share.Entities;
using DE.Share.Rpc;

namespace Assets.Scripts.Demo.Client.Entities
{
    public partial class BasicInfoComponent : EntityComponent
    {
        [EntityProperty(EntityPropertyFlag.ClientServer)]
        private string __HeadIcon = "";

        [EntityProperty(EntityPropertyFlag.ClientServer)]
        private int __Score = 0;

        [ClientRpc]
        public void NotifyHeadIconChanged(string headIcon)
        {
            HeadIcon = headIcon ?? string.Empty;
        }
    }
}
