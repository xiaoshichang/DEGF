using System;
using System.Linq;
using System.Text.Json;

namespace DE.Server.NativeBridge
{
    internal sealed class CreateAvatarCommand
    {
        public string OriginServerId { get; set; } = string.Empty;
        public string OwnerGateId { get; set; } = string.Empty;
        public ulong ClientSessionId { get; set; }
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string Error { get; set; } = string.Empty;
        public byte[] AvatarData { get; set; } = Array.Empty<byte>();
    }

    public sealed partial class GameServerRuntimeState
    {
        public bool HandleSystemCommand(string sourceServerId, byte[] payload)
        {
            if (!SystemCommandPayload.TryDeserialize(payload, 0, payload?.Length ?? 0, out var command)
                || command.TargetServerId != _managedRuntimeState.ServerId
                || (sourceServerId != _managedRuntimeState.ServerId && !_managedRuntimeState.GetGateServerIds().Contains(sourceServerId)))
            {
                return false;
            }
            switch (command.Kind)
            {
            case SystemCommandKind.AvatarMigration:
                return HandleAvatarMigrationGameMessage(sourceServerId, command);
            case SystemCommandKind.CreateEntity:
                return HandleCreateEntityCommand(sourceServerId, command);
            case SystemCommandKind.CreateAvatar:
                return HandleCreateAvatarCommand(sourceServerId, command);
            case SystemCommandKind.CreateAvatarResponse:
                return TryReadCreateAvatarCommand(command, out var result) && result.ClientSessionId == 0
                    && result.OriginServerId == _managedRuntimeState.ServerId;
            default:
                return false;
            }
        }

        internal static bool TryReadCreateAvatarCommand(SystemCommandPayload command, out CreateAvatarCommand data)
        {
            data = null;
            if (command.EntityId == Guid.Empty || command.CommandId != 0 || command.Data == null)
            {
                return false;
            }
            try
            {
                data = JsonSerializer.Deserialize<CreateAvatarCommand>(command.Data);
                return data != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private bool HandleCreateAvatarCommand(string sourceServerId, SystemCommandPayload command)
        {
            if (!TryReadCreateAvatarCommand(command, out var request)
                || (!_managedRuntimeState.GetGameServerIds().Contains(request.OriginServerId)
                    && !_managedRuntimeState.GetGateServerIds().Contains(request.OriginServerId))
                || (request.ClientSessionId != 0 && (request.OwnerGateId != request.OriginServerId || request.OwnerGateId != sourceServerId
                    || !_managedRuntimeState.GetGateServerIds().Contains(request.OwnerGateId))))
            {
                return false;
            }
            var result = CreateAvatarLocal(command.EntityId, request.OwnerGateId);
            request.Success = result.IsSuccess;
            request.StatusCode = result.StatusCode;
            request.Error = result.Error;
            request.AvatarData = result.AvatarData;
            var sent = SystemCommandCaller.Send(new SystemCommandPayload
            {
                Version = SystemCommandPayload.CurrentVersion,
                Kind = SystemCommandKind.CreateAvatarResponse,
                EntityId = command.EntityId,
                TargetServerId = request.OriginServerId,
                Data = JsonSerializer.SerializeToUtf8Bytes(request),
            });
            if (!sent)
            {
                return false;
            }
            if (result.IsSuccess)
            {
                // The response and subsequent client RPCs share the same ordered Game-to-Gate connection.
                NotifyAvatarClientAttached(Avatars[command.EntityId], request.ClientSessionId);
            }
            return true;
        }

        internal static bool SendCreateAvatarCommand(string targetServerId, Guid avatarId, string ownerGateId, ulong clientSessionId)
        {
            return SystemCommandCaller.Send(new SystemCommandPayload
            {
                Version = SystemCommandPayload.CurrentVersion,
                Kind = SystemCommandKind.CreateAvatar,
                EntityId = avatarId,
                TargetServerId = targetServerId,
                Data = JsonSerializer.SerializeToUtf8Bytes(new CreateAvatarCommand
                {
                    OriginServerId = ManagedRuntimeState.RequireCurrent().ServerId,
                    OwnerGateId = ownerGateId,
                    ClientSessionId = clientSessionId,
                }),
            });
        }
    }
}
