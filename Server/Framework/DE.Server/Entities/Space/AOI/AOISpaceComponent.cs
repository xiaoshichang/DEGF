using System;
using System.Collections.Generic;
using System.Linq;
using DE.Server.AOI;
using DE.Server.NativeBridge;
using DE.Share.Entities;

namespace DE.Server.Entities
{
    public class AOISpaceComponent : ServerEntityComponent
    {
        public IAOISpace Space { get; private set; }

        protected override void Initialize()
        {
            base.Initialize();
            Space = CreateSpace();
            Space.AgentEventEnter += OnAgentEventEnter;
            Space.AgentEventLeave += OnAgentEventLeave;
        }

        protected override void UnInitialize()
        {
            if (Space != null)
            {
                Space.AgentEventEnter -= OnAgentEventEnter;
                Space.AgentEventLeave -= OnAgentEventLeave;
                Space.Clear();
                Space = null;
            }
            base.UnInitialize();
        }

        protected virtual IAOISpace CreateSpace()
        {
            return new AOISpace();
        }

        public bool Contains(Guid agentId)
        {
            return Space?.Agents.ContainsKey(agentId) ?? false;
        }

        public bool Enter(AOIAgent agent)
        {
            if (!IsInitialized)
            {
                DELogger.Error(nameof(AOISpaceComponent), "Cannot enter an uninitialized AOI Space.");
                return false;
            }
            return Space.Enter(agent);
        }

        public bool Leave(AOIAgent agent)
        {
            return Space.Leave(agent);
        }

        public IReadOnlyCollection<AOIAgent> GetVisibleAgents(Guid observerId)
        {
            return Space.Agents[observerId].ObservingAgents;
        }

        public bool AddAOIAgent(ServerEntity entity)
        {
            var spaceId = OwnerEntity.Guid;
            if (entity == null)
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Cannot publish a null entity, spaceId={spaceId}.");
                return false;
            }
            if (entity.Guid == spaceId)
            {
                DELogger.Error(nameof(AOISpaceComponent), $"A Space cannot publish itself as an AOI agent, spaceId={spaceId}.");
                return false;
            }
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (!runtime.Entities.TryGetValue(entity.Guid, out var registered))
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Cannot publish an unregistered entity, spaceId={spaceId}, entityId={entity.Guid}.");
                return false;
            }
            if (!ReferenceEquals(entity, registered))
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Another entity uses this ID, spaceId={spaceId}, entityId={entity.Guid}.");
                return false;
            }
            if (Contains(entity.Guid))
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Entity is already published in AOI, spaceId={spaceId}, entityId={entity.Guid}.");
                return false;
            }
            var component = entity.Components.OfType<AOIAgentComponent>().SingleOrDefault();
            if (component == null)
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Entity has no AOIAgentComponent, spaceId={spaceId}, entityId={entity.Guid}.");
                return false;
            }
            if (entity is ISpatialEntity spatialEntity)
            {
                component.Agent.Position = spatialEntity.Position;
            }
            return Enter(component.Agent);
        }

        public bool RemoveAOIAgent(ServerEntity entity)
        {
            if (entity == null)
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Cannot remove a null entity from AOI, spaceId={OwnerEntity.Guid}.");
                return false;
            }
            // Prepared migration candidates may leave without ever becoming visible.
            if (!Contains(entity.Guid))
            {
                return false;
            }
            return Leave(Space.Agents[entity.Guid]);
        }

        private void OnAgentEventEnter(AOIAgent observer, AOIAgent target)
        {
            if (!ReferenceEquals(observer.Space, Space))
            {
                return;
            }
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (runtime.Entities[observer.Id] is not ServerAvatarEntity avatar)
            {
                return;
            }
            if (!avatar.IsInitialized)
            {
                return;
            }
            var targetEntity = runtime.Entities[target.Id];
            if (!targetEntity.IsInitialized)
            {
                return;
            }
            avatar.CallClient("NotifySpaceEntityAppeared", OwnerEntity.Guid, targetEntity.GetType().Name,
                EntitySerializer.Serialize(targetEntity, EntitySerializeReason.Broadcase));
        }

        private void OnAgentEventLeave(AOIAgent observer, AOIAgent target)
        {
            // A departing observer no longer needs deltas for the old Space.
            if (!ReferenceEquals(observer.Space, Space))
            {
                return;
            }
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (runtime.Entities[observer.Id] is not ServerAvatarEntity avatar)
            {
                return;
            }
            if (!avatar.IsInitialized)
            {
                return;
            }
            avatar.CallClient("NotifySpaceEntityDisappeared", OwnerEntity.Guid, target.Id);
        }
    }
}
