using System;
using System.Collections.Generic;
using DE.Server.AOI;
using DE.Server.NativeBridge;
using DE.Share.Entities;

namespace DE.Server.Entities
{
    public partial class AOISpaceComponent : EntityComponent
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
    }
}
