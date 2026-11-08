using System;
using System.Collections.Generic;
using DE.Share.Foundation.Mathematics;

namespace DE.Server.AOI
{
    public interface IAOIAgent
    {
        Guid Id { get; }
        Vector3 Position { get; }
        bool IsObserver { get; }
        IAOISpace Space { get; }
        IReadOnlyCollection<AOIAgent> ObservingAgents { get; }
        IReadOnlyCollection<AOIAgent> Observers { get; }
    }
}
