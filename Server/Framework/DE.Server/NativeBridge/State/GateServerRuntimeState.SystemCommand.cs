using System.Linq;
using DE.Server.Entities;

namespace DE.Server.NativeBridge
{
    public sealed partial class GateServerRuntimeState
    {
        public bool HandleSystemCommand(string sourceServerId, byte[] payload)
        {
            if (!SystemCommandPayload.TryDeserialize(payload, 0, payload?.Length ?? 0, out var command)
                || !_managedRuntimeState.GetGameServerIds().Contains(sourceServerId))
            {
                return false;
            }
            if (command.TargetServerId == _managedRuntimeState.ServerId)
            {
                switch (command.Kind)
                {
                case SystemCommandKind.AvatarMigration:
                    return HandleAvatarMigrationGateMessage(sourceServerId, command);
                case SystemCommandKind.CreateAvatarResponse:
                    return GameServerRuntimeState.TryReadCreateAvatarCommand(command, out var response)
                        && response.OriginServerId == _managedRuntimeState.ServerId
                        && HandleCreateAvatarRsp(sourceServerId, command.EntityId, response.ClientSessionId,
                            response.Success, response.StatusCode, response.Error, response.AvatarData);
                default:
                    return false;
                }
            }
            if (!_managedRuntimeState.GetGameServerIds().Contains(command.TargetServerId))
            {
                return false;
            }
            if (command.Kind == SystemCommandKind.CreateEntity
                && (!RemoteEntityCaller.TryReadCommand(command, out var creation) || creation.OriginServerId != sourceServerId))
            {
                return false;
            }
            if (command.Kind == SystemCommandKind.CreateAvatar
                && (!GameServerRuntimeState.TryReadCreateAvatarCommand(command, out var request) || request.OriginServerId != sourceServerId
                    || request.ClientSessionId != 0 || !string.IsNullOrEmpty(request.OwnerGateId)))
            {
                return false;
            }
            return NativeAPI.SendSystemCommandToServer(command.TargetServerId, payload);
        }
    }
}
