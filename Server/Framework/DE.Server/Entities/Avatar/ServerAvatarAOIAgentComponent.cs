using DE.Server.AOI;
using DE.Share.Foundation.Mathematics;

namespace DE.Server.Entities
{
    public class ServerAvatarAOIAgentComponent : ServerEntityComponent
    {
        public AOIAgent Agent { get; private set; }

        /// <summary>使用已恢复的位置创建 Avatar 的 AOIAgent。</summary>
        protected override void Initialize()
        {
            base.Initialize();
            Agent = CreateAgent((ServerAvatarEntity)OwnerEntity);
        }

        /// <summary>让 Agent 离开 AOI 空间并释放组件持有的引用。</summary>
        protected override void UnInitialize()
        {
            Agent?.LeaveSpace();
            Agent = null;
            base.UnInitialize();
        }

        protected virtual AOIAgent CreateAgent(ServerAvatarEntity avatar)
        {
            return new AOIAgent(avatar.Guid, avatar.TransformCmpt.Position);
        }

        public void Move(Vector3 position)
        {
            // Property restoration precedes initialization; CreateAgent reads the restored position.
            Agent?.Move(position);
        }
    }
}
