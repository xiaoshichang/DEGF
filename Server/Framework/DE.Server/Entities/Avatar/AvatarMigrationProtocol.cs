using System;
using System.Collections.Generic;
using System.Text.Json;
using DE.Server.Entities;
using DE.Share.Rpc;

namespace DE.Server.NativeBridge
{
    // KBEngine 2.5.12: Base onMigrationCellappStart/End, Cell reqTeleportToCellApp/CB/Over.
    internal enum AvatarMigrationCommand : ushort
    {
        MigrationStart = 1,
        TeleportToGame = 2,
        TeleportResult = 3,
        MigrationEnd = 4,
        MigrationOver = 5,
        // DEGF input-order extension; TeleportResult fences the source's earlier client input.
        SourceInputDrained = 6,
    }

    internal sealed class AvatarMigrationMessage
    {
        public const ushort CurrentVersion = 3;
        public ushort Version { get; set; } = CurrentVersion;
        public AvatarMigrationCommand Command { get; set; }
        public Guid MigrationId { get; set; }
        public Guid AvatarId { get; set; }
        public Guid TargetSpaceId { get; set; }
        public string SourceGameServerId { get; set; } = string.Empty;
        public string TargetGameServerId { get; set; } = string.Empty;
        public string GateServerId { get; set; } = string.Empty;
        public ulong ClientSessionId { get; set; }
        public bool Success { get; set; } = true;
        public string Error { get; set; } = string.Empty;
        public byte[] AvatarData { get; set; } = Array.Empty<byte>();

        public AvatarMigrationMessage CreateResponse(AvatarMigrationCommand command, bool success = true, string error = null)
        {
            return new AvatarMigrationMessage
            {
                Command = command,
                MigrationId = MigrationId,
                AvatarId = AvatarId,
                TargetSpaceId = TargetSpaceId,
                SourceGameServerId = SourceGameServerId,
                TargetGameServerId = TargetGameServerId,
                GateServerId = GateServerId,
                ClientSessionId = ClientSessionId,
                Success = success,
                Error = error ?? string.Empty,
            };
        }
    }

    internal static class AvatarMigrationProtocol
    {
        private const int MaxPayloadSizeBytes = 16 * 1024 * 1024;
        private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
        };

        public static byte[] BuildSystemCommandPayload(string targetServerId, AvatarMigrationMessage message)
        {
            return new SystemCommandPayload
            {
                Version = SystemCommandPayload.CurrentVersion,
                Kind = SystemCommandKind.AvatarMigration,
                EntityId = message.AvatarId,
                TargetServerId = targetServerId ?? string.Empty,
                CommandId = (uint)message.Command,
                Data = JsonSerializer.SerializeToUtf8Bytes(message, s_jsonOptions),
            }.Serialize();
        }

        public static bool TryDeserialize(SystemCommandPayload envelope, out AvatarMigrationMessage message)
        {
            message = null;
            if (envelope.Kind != SystemCommandKind.AvatarMigration || envelope.Data == null
                || envelope.Data.Length == 0 || envelope.Data.Length > MaxPayloadSizeBytes)
            {
                return false;
            }
            try
            {
                message = JsonSerializer.Deserialize<AvatarMigrationMessage>(envelope.Data, s_jsonOptions);
            }
            catch (JsonException)
            {
                return false;
            }
            return message != null && message.Version == AvatarMigrationMessage.CurrentVersion
                && Enum.IsDefined(typeof(AvatarMigrationCommand), message.Command)
                && message.MigrationId != Guid.Empty && message.AvatarId != Guid.Empty && message.TargetSpaceId != Guid.Empty
                && message.AvatarId == envelope.EntityId && (uint)message.Command == envelope.CommandId
                && !string.IsNullOrWhiteSpace(message.SourceGameServerId)
                && !string.IsNullOrWhiteSpace(message.TargetGameServerId)
                && message.SourceGameServerId != message.TargetGameServerId
                && !string.IsNullOrWhiteSpace(message.GateServerId);
        }
    }

    internal sealed class SourceAvatarMigrationContext
    {
        public ServerAvatarEntity Avatar;
        public AvatarMigrationMessage Message;
        public Guid SourceSpaceId;
        public byte[] AvatarData = Array.Empty<byte>();
    }

    internal sealed class TargetAvatarMigrationContext
    {
        public ServerAvatarEntity Avatar;
        public AvatarMigrationMessage Message;
        // KBE reqTeleportToCellApp sets success=true only after enter-world and teleport callbacks return (line 2004).
        public bool EntryCompleted;
    }

    // KBEngine entity_macro.h: ENTITY_FLAGS_TELEPORT_START / ENTITY_FLAGS_TELEPORT_STOP.
    [Flags]
    internal enum GateAvatarMigrationFlags
    {
        None = 0,
        TeleportStart = 1,
        TeleportStop = 2,
    }

    internal sealed class GateAvatarMigrationContext
    {
        public AvatarMigrationMessage Message;
        public GateAvatarMigrationFlags Flags;
        // DEGF-only completion condition; Avatar roles and KBE Start/End flags stay unchanged.
        public bool SourceInputDrained;
        public readonly Queue<byte[]> ClientInputs = new Queue<byte[]>();
        // BaseMessagesForwardClientHandler buffers client output when End arrives before Start.
        public readonly Queue<byte[]> ClientMessages = new Queue<byte[]>();
    }

    // KBEngine GhostManager: a short-lived route survives removal of the source entity.
    internal sealed class AvatarForwardingRoute
    {
        public AvatarMigrationMessage Message;
        public DateTime ExpiresAtUtc;
    }
}
