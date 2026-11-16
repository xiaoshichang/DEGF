using DE.Share.Entities;
using DE.Share.Foundation.Mathematics;

namespace DE.Server.Entities
{
    public partial class ServerAvatarTransformComponent : ServerEntityComponent
    {
        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Vector3 __Position;

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Quaternion __Rotation;

        /// <summary>初始化位置和旋转默认值，不依赖其他组件；默认属性随后可由快照覆盖。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            __Position = default;
            __Rotation = Quaternion.Identity;
        }

        partial void OnAfterPositionChange(Vector3 value)
        {
            ((ServerAvatarEntity)OwnerEntity).AOIAgentCmpt.Move(value);
        }
    }
}
