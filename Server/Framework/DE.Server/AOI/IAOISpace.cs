using System;
using System.Collections.Generic;
using DE.Share.Foundation.Mathematics;

namespace DE.Server.AOI
{
    public interface IAOISpace
    {
        IReadOnlyDictionary<Guid, AOIAgent> Agents { get; }
        event Action<AOIAgent, AOIAgent> AgentEventEnter;
        event Action<AOIAgent, AOIAgent> AgentEventLeave;
        bool Enter(AOIAgent agent);
        bool Leave(AOIAgent agent);
        bool Move(AOIAgent agent, Vector3 position);
        void Clear();
    }
}
