using DE.Share.Entities;
using DE.Client.Entities;

namespace Assets.Scripts.Demo.Client.Entities
{
    [UnityEngine.Scripting.Preserve]
    public partial class DemoClientAvatarEntity : ClientAvatarEntity
    {
        /// <summary>构造基础信息组件并初始化其默认属性与字段。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            BasicInfo = AddComponent(new ClientAvatarBasicInfoComponent());
        }
        [EntityComponent]
        public ClientAvatarBasicInfoComponent BasicInfo { get; private set; }
    }
}
