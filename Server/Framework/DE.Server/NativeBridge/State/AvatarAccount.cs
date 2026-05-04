using System;

namespace DE.Server.NativeBridge
{
    public sealed class AvatarAccount
    {
        public Guid AvatarId { get; set; }
        public string Account { get; set; } = string.Empty;
        public ulong ClientSessionId { get; set; }
        public string GameServerId { get; set; } = string.Empty;
    }
}
