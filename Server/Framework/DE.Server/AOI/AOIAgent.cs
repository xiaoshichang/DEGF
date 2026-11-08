using System;
using System.Collections.Generic;
using DE.Share.Foundation.Mathematics;

namespace DE.Server.AOI
{
    public class AOIAgent : IAOIAgent
    {
        private readonly Dictionary<Guid, AOIAgent> _observingAgentsById = new();
        private readonly Dictionary<Guid, AOIAgent> _observersById = new();

        public AOIAgent(Guid id, Vector3 position = default, bool isObserver = true)
        {
            Id = id;
            Position = position;
            IsObserver = isObserver;
        }

        public Guid Id { get; }
        public Vector3 Position { get; internal set; }
        public bool IsObserver { get; }
        public IAOISpace Space { get; internal set; }
        public IReadOnlyCollection<AOIAgent> ObservingAgents => _observingAgentsById.Values;
        public IReadOnlyCollection<AOIAgent> Observers => _observersById.Values;

        internal bool IsObserving(AOIAgent target)
        {
            return _observingAgentsById.ContainsKey(target.Id);
        }

        internal void Observe(AOIAgent target)
        {
            _observingAgentsById.Add(target.Id, target);
            target._observersById.Add(Id, this);
        }

        internal void StopObserving(AOIAgent target)
        {
            _observingAgentsById.Remove(target.Id);
            target._observersById.Remove(Id);
        }
    }
}
