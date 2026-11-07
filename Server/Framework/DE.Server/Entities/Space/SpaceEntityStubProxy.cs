using System;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public enum SpaceCreationState
    {
        Planned,
        Creating,
        Created,
        Failed,
    }

    /// <summary>The Stub's proxy for a remote Space, including creation progress and reported statistics.</summary>
    public sealed class SpaceEntityStubProxy
    {
        public SpaceEntityStubProxy(Guid spaceId, int spaceNo, string gameServerId, int maxPlayers)
        {
            SpaceNo = spaceNo;
            MailBox = new EntityMailBox(spaceId, gameServerId);
            MaxPlayers = maxPlayers;
        }

        public Guid SpaceId => MailBox.EntityId;
        public int SpaceNo { get; }
        public EntityMailBox MailBox { get; }
        public int MaxPlayers { get; }
        public SpaceCreationState CreationState { get; internal set; } = SpaceCreationState.Planned;
        public string CreationError { get; internal set; } = string.Empty;
        public bool IsReady => CreationState == SpaceCreationState.Created;
        public int PlayerCount { get; internal set; }

        /// <summary>UTC Unix time in milliseconds supplied by the Space, not a sequence number.</summary>
        public ulong ReportTimeMilliseconds { get; internal set; }
        public bool HasCapacity => MaxPlayers == 0 || PlayerCount < MaxPlayers;
    }
}
