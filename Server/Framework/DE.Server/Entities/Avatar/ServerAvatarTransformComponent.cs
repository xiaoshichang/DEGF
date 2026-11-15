using DE.Share.Entities;
using DE.Share.Foundation.Mathematics;

namespace DE.Server.Entities
{
    public partial class ServerAvatarTransformComponent : ServerEntityComponent, IEntityTransform
    {
        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Vector3 __Position;

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Quaternion __Rotation;

        public EntityTransform Transform { get; private set; }

        /// <summary>初始化变换默认属性和本地字段，不依赖其他组件；默认属性随后可由快照覆盖。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            __Position = default;
            __Rotation = Quaternion.Identity;
            Transform = new EntityTransform(this);
        }

        /// <summary>组件依赖清理完成后释放变换字段引用。</summary>
        protected override void PostUnInitialize()
        {
            Transform = null;
            base.PostUnInitialize();
        }

        public void SetTransform(Vector3 position, Quaternion rotation)
        {
            Transform.SetTransform(position, rotation);
        }

        partial void OnAfterPositionChange(Vector3 value)
        {
            ((ServerAvatarEntity)OwnerEntity).AOIAgentCmpt.Move(value);
        }
    }
}
