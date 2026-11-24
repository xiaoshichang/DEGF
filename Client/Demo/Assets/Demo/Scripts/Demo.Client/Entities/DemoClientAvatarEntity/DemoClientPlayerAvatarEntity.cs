namespace Assets.Scripts.Demo.Client.Entities
{
    /// <summary>当前登录玩家的 Avatar，承载仅本人需要的客户端逻辑。</summary>
    [UnityEngine.Scripting.Preserve]
    public partial class DemoClientPlayerAvatarEntity : DemoClientAvatarEntity
    {
        /// <summary>在组件初始化完成后，为本人安装 Demo 空间加载回调。</summary>
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
    }
}
