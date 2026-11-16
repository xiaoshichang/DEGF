using DE.Client.Entities;
using DE.Share.Rpc;

namespace Assets.Scripts.Demo.Client.Entities
{
    public partial class ClientAvatarBasicInfoComponent : ClientEntityComponent
    {
        [EntityProperty(EntityPropertyFlag.ClientServer)]
        private string __HeadIcon;

        [EntityProperty(EntityPropertyFlag.ClientServer)]
        private int __Score;

        /// <summary>初始化基础信息默认属性，随后由服务器快照覆盖。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            __HeadIcon = string.Empty;
            __Score = 0;
        }

        [ClientRpc]
        public void NotifyHeadIconChanged(string headIcon)
        {
            HeadIcon = headIcon ?? string.Empty;
        }
    }
}
