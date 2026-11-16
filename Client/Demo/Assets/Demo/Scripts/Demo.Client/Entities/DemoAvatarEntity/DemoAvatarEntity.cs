using DE.Client.Entities;

namespace Assets.Scripts.Demo.Client.Entities
{
    public partial class DemoAvatarEntity : ClientAvatarEntity
    {
        /// <summary>构造基础信息组件并初始化其默认属性与字段。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            BasicInfo = AddComponent(new ClientAvatarBasicInfoComponent(), nameof(BasicInfo));
        }



        /// <summary>在组件初始化完成后安装 Demo 空间加载回调。</summary>
        protected override void PostInitialize()
        {
            base.PostInitialize();
            TransformCmpt.Spaces.Load = context =>
            {
                // The Demo has no per-Space static resource mapping yet. Replace this loader to load resources by SpaceNo.
                Assets.Scripts.DE.Client.Core.DELogger.Info("Space loading, spaceNo=" + context.SpaceNo + ", spaceId=" + context.Space.Guid);
                context.Complete();
            };
        }

        public ClientAvatarBasicInfoComponent BasicInfo { get; private set; }
    }
}
