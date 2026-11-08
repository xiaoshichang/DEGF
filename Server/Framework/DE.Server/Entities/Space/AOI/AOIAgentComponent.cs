using DE.Server.AOI;
using DE.Share.Entities;
using DE.Share.Foundation.Mathematics;

namespace DE.Server.Entities
{
    public class AOIAgentComponent : ServerEntityComponent
    {
        public AOIAgent Agent { get; private set; }
        public IAOISpace CurrentSpace => Agent?.Space;

        protected override void Initialize()
        {
            base.Initialize();
            Agent = CreateAgent(OwnerEntity);
        }

        protected override void UnInitialize()
        {
            CurrentSpace?.Leave(Agent);
            Agent = null;
            base.UnInitialize();
        }

        protected virtual AOIAgent CreateAgent(Entity entity)
        {
            var position = entity is ISpatialEntity spatialEntity ? spatialEntity.Position : default;
            return new AOIAgent(entity.Guid, position);
        }

        public void Move(Vector3 position)
        {
            // Property restoration runs before component initialization; CreateAgent reads the restored position.
            if (Agent == null)
            {
                return;
            }
            if (CurrentSpace == null)
            {
                Agent.Position = position;
                return;
            }
            CurrentSpace.Move(Agent, position);
        }
    }
}
