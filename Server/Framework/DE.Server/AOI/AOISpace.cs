using System;
using System.Collections.Generic;
using System.Linq;
using DE.Server.NativeBridge;
using DE.Share.Foundation.Mathematics;

namespace DE.Server.AOI
{
    /// <summary>Default AOI: every observing agent sees every other agent in the same Space.</summary>
    public class AOISpace : IAOISpace
    {
        private readonly Dictionary<Guid, AOIAgent> _agentsById = new();

        public IReadOnlyDictionary<Guid, AOIAgent> Agents => _agentsById;
        public event Action<AOIAgent, AOIAgent> AgentEventEnter;
        public event Action<AOIAgent, AOIAgent> AgentEventLeave;

        public virtual bool Enter(AOIAgent agent)
        {
            if (agent == null)
            {
                DELogger.Error(nameof(AOISpace), "Cannot enter an AOI Space with a null agent.");
                return false;
            }
            if (agent.Space != null)
            {
                DELogger.Error(nameof(AOISpace), $"AOI agent is already in a Space, agentId={agent.Id}.");
                return false;
            }
            if (!_agentsById.TryAdd(agent.Id, agent))
            {
                DELogger.Error(nameof(AOISpace), $"Another AOI agent uses this ID, agentId={agent.Id}.");
                return false;
            }
            agent.Space = this;
            ApplyVisibility(EvaluateVisibility(agent));
            return true;
        }

        public virtual bool Leave(AOIAgent agent)
        {
            if (!IsMember(agent))
            {
                return false;
            }
            var changes = new List<(AOIAgent Observer, AOIAgent Target, bool IsVisible)>();
            foreach (var target in agent.ObservingAgents.ToArray())
            {
                agent.StopObserving(target);
                changes.Add((agent, target, false));
            }
            foreach (var observer in agent.Observers.ToArray())
            {
                observer.StopObserving(agent);
                changes.Add((observer, agent, false));
            }
            _agentsById.Remove(agent.Id);
            agent.Space = null;
            PublishChanges(changes);
            return true;
        }

        public virtual bool Move(AOIAgent agent, Vector3 position)
        {
            if (!IsMember(agent))
            {
                return false;
            }
            agent.Position = position;
            ApplyVisibility(EvaluateVisibility(agent));
            return true;
        }

        public virtual void Clear()
        {
            foreach (var agent in _agentsById.Values.ToArray())
            {
                Leave(agent);
            }
        }

        /// <summary>Override the visibility rule without changing Space membership or observation relationships.</summary>
        protected virtual bool CanObserve(AOIAgent observer, AOIAgent target)
        {
            return true;
        }

        private bool IsMember(AOIAgent agent)
        {
            if (agent == null)
            {
                DELogger.Error(nameof(AOISpace), "Cannot access AOI Space membership with a null agent.");
                return false;
            }
            if (!ReferenceEquals(agent.Space, this))
            {
                DELogger.Error(nameof(AOISpace), $"AOI agent belongs to a different Space, agentId={agent.Id}.");
                return false;
            }
            return true;
        }

        private List<(AOIAgent Observer, AOIAgent Target, bool IsVisible)> EvaluateVisibility(AOIAgent agent)
        {
            var visibility = new List<(AOIAgent Observer, AOIAgent Target, bool IsVisible)>();
            foreach (var other in _agentsById.Values)
            {
                if (ReferenceEquals(other, agent))
                {
                    continue;
                }
                EvaluatePair(other, agent, visibility);
                EvaluatePair(agent, other, visibility);
            }
            return visibility;
        }

        private void EvaluatePair(AOIAgent observer, AOIAgent target,
            List<(AOIAgent Observer, AOIAgent Target, bool IsVisible)> changes)
        {
            bool isVisible = observer.IsObserver && CanObserve(observer, target);
            if (isVisible != observer.IsObserving(target))
            {
                changes.Add((observer, target, isVisible));
            }
        }

        private void ApplyVisibility(List<(AOIAgent Observer, AOIAgent Target, bool IsVisible)> visibility)
        {
            foreach (var pair in visibility)
            {
                if (pair.IsVisible)
                {
                    pair.Observer.Observe(pair.Target);
                }
                else
                {
                    pair.Observer.StopObserving(pair.Target);
                }
            }
            PublishChanges(visibility);
        }

        private void PublishChanges(List<(AOIAgent Observer, AOIAgent Target, bool IsVisible)> changes)
        {
            foreach (var change in changes)
            {
                if (change.IsVisible)
                {
                    AgentEventEnter?.Invoke(change.Observer, change.Target);
                }
                else
                {
                    AgentEventLeave?.Invoke(change.Observer, change.Target);
                }
            }
        }
    }
}
