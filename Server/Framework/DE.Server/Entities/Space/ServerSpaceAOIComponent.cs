using System;
using System.Collections.Generic;
using DE.Server.AOI;
using DE.Server.NativeBridge;

namespace DE.Server.Entities
{
    public class ServerSpaceAOIComponent : ServerEntityComponent
    {
        public IAOISpace Space { get; private set; }

        /// <summary>构造本地 AOI 模型字段，不建立事件依赖。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            Space = CreateSpace();
        }

        /// <summary>建立 AOI 模型与空间实体间的可见性事件依赖。</summary>
        protected override void Initialize()
        {
            base.Initialize();
            Space.AgentEventEnter += OnAgentEventEnter;
            Space.AgentEventLeave += OnAgentEventLeave;
        }

        /// <summary>取消事件依赖并移除全部 Agent，模型字段保留至 PostUnInitialize。</summary>
        protected override void UnInitialize()
        {
            if (Space != null)
            {
                Space.AgentEventEnter -= OnAgentEventEnter;
                Space.AgentEventLeave -= OnAgentEventLeave;
                Space.Clear();
            }
            base.UnInitialize();
        }

        /// <summary>依赖解除后释放 AOI 模型字段。</summary>
        protected override void PostUnInitialize()
        {
            Space = null;
            base.PostUnInitialize();
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

        public bool AddAOIAgent(ServerAvatarEntity entity)
        {
            var spaceId = OwnerEntity.Guid;
            if (entity == null)
            {
                DELogger.Error(nameof(ServerSpaceAOIComponent), $"Cannot publish a null entity, spaceId={spaceId}.");
                return false;
            }
            if (entity.Guid == spaceId)
            {
                DELogger.Error(nameof(ServerSpaceAOIComponent), $"A Space cannot publish itself as an AOI agent, spaceId={spaceId}.");
                return false;
            }
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (!runtime.Entities.TryGetValue(entity.Guid, out var registered))
            {
                DELogger.Error(nameof(ServerSpaceAOIComponent), $"Cannot publish an unregistered entity, spaceId={spaceId}, entityId={entity.Guid}.");
                return false;
            }
            if (!ReferenceEquals(entity, registered))
            {
                DELogger.Error(nameof(ServerSpaceAOIComponent), $"Another entity uses this ID, spaceId={spaceId}, entityId={entity.Guid}.");
                return false;
            }
            if (Contains(entity.Guid))
            {
                DELogger.Error(nameof(ServerSpaceAOIComponent), $"Entity is already published in AOI, spaceId={spaceId}, entityId={entity.Guid}.");
                return false;
            }
            var component = entity.AOIAgentCmpt;
            if (component?.Agent == null)
            {
                DELogger.Error(nameof(ServerSpaceAOIComponent), "Avatar has no initialized ServerAvatarAOIAgentComponent, "
                    + $"spaceId={spaceId}, entityId={entity.Guid}.");
                return false;
            }
            if (component.Agent.Space == null)
            {
                component.Agent.Move(entity.TransformCmpt.Position);
            }
            return Enter(component.Agent);
        }

        public bool RemoveAOIAgent(ServerEntity entity)
        {
            if (entity == null)
            {
                DELogger.Error(nameof(ServerSpaceAOIComponent), $"Cannot remove a null entity from AOI, spaceId={OwnerEntity.Guid}.");
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
            // The entry snapshot supplies the initial visible set before later AOI notifications are sent.
            if (!avatar.IsInitialized || !avatar.SpaceCmpt.IsSpaceEntryNotified
                || !ReferenceEquals(avatar.SpaceCmpt.CurrentSpace, OwnerEntity))
            {
                return;
            }
            var targetEntity = runtime.Entities[target.Id];
            if (!targetEntity.IsInitialized)
            {
                return;
            }
            avatar.CallClient("NotifySpaceEntityAppeared", OwnerEntity.Guid, targetEntity.GetType().Name,
                EntitySerializer.SerializeSnapshot(targetEntity, EntitySerializationProfile.Observer));
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
            if (!avatar.IsInitialized || !avatar.SpaceCmpt.IsSpaceEntryNotified
                || !ReferenceEquals(avatar.SpaceCmpt.CurrentSpace, OwnerEntity))
            {
                return;
            }
            avatar.CallClient("NotifySpaceEntityDisappeared", OwnerEntity.Guid, target.Id);
        }
    }
}
