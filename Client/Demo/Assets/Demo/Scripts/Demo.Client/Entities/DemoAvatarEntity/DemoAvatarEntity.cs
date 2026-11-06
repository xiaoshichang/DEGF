using DE.Share.Entities;

namespace Assets.Scripts.Demo.Client.Entities
{
    public partial class DemoAvatarEntity : AvatarEntity
    {
        public DemoAvatarEntity()
        {
            BasicInfo = AddComponent(new BasicInfoComponent());
        }

        [EntityProperty(EntityPropertyFlag.ClientServer)]
        private string __HeadIcon;

        [EntityProperty(EntityPropertyFlag.ClientServer)]
        private int __Score;

        protected override void PreInitialize()
        {
            base.PreInitialize();
            HeadIcon = string.Empty;
            Score = 0;
        }

        protected override void PostInitialize()
        {
            base.PostInitialize();
            Spaces.Load = context =>
            {
                // The Demo has no per-Space static resource mapping yet. Replace this loader to load resources by SpaceNo.
                Assets.Scripts.DE.Client.Core.DELogger.Info("Space loading, spaceNo=" + context.SpaceNo + ", spaceId=" + context.Space.Guid);
                context.Complete();
            };
        }

        public BasicInfoComponent BasicInfo { get; }
    }
}
