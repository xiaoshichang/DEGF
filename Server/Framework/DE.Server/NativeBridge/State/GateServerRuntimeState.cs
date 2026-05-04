using System;
using System.Collections.Generic;
using DE.Server.Auth;

namespace DE.Server.NativeBridge
{
    public sealed class GateServerRuntimeState
    {
        private readonly ManagedRuntimeState _managedRuntimeState;

        public GateServerRuntimeState(ManagedRuntimeState managedRuntimeState)
        {
            _managedRuntimeState = managedRuntimeState ?? throw new ArgumentNullException(nameof(managedRuntimeState));
        }

        public Dictionary<Guid, AvatarAccount> AvatarIdToAccount { get; } = new Dictionary<Guid, AvatarAccount>();

        public GateAuthValidationResult ValidateAuth(GateAuthValidationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            IReadOnlyList<string> gateServerIds = request.GateServerIds;
            if (gateServerIds == null)
            {
                gateServerIds = Array.Empty<string>();
            }

            return GateAuthValidator.Validate(
                _managedRuntimeState.ServerId,
                request.Account,
                request.Password,
                gateServerIds
            );
        }

        public bool HandleAvatarLoginReq(ulong clientSessionId, string account)
        {
            var accountName = string.IsNullOrWhiteSpace(account)
                ? "account-" + Guid.NewGuid().ToString("N")
                : account.Trim();
            var avatarId = Guid.NewGuid();
            AvatarIdToAccount[avatarId] = new AvatarAccount
            {
                AvatarId = avatarId,
                Account = accountName,
                ClientSessionId = clientSessionId,
            };

            DELogger.Info(
                nameof(GateServerRuntimeState),
                $"Avatar login request accepted, account={accountName}, clientSessionId={clientSessionId}, avatarId={avatarId}."
            );
            return CreateAvatarRemote(avatarId);
        }

        public bool CreateAvatarRemote(Guid avatarId)
        {
            if (avatarId == Guid.Empty)
            {
                return NativeAPI.SendAvatarLoginRsp(0, Guid.Empty, false, 400, "avatar id is empty", Array.Empty<byte>());
            }

            var gameServerId = _managedRuntimeState.SelectGameServerId(avatarId);
            if (string.IsNullOrWhiteSpace(gameServerId))
            {
                return TrySendLoginRspByAvatarId(avatarId, false, 503, "no game server is available");
            }

            var sent = NativeAPI.SendCreateAvatarReq(gameServerId, avatarId);
            if (!sent)
            {
                return TrySendLoginRspByAvatarId(avatarId, false, 503, "failed to send create avatar request");
            }

            DELogger.Info(
                nameof(GateServerRuntimeState),
                $"Requested avatar creation, avatarId={avatarId}, gameServerId={gameServerId}."
            );
            return true;
        }

        public bool HandleCreateAvatarRsp(string sourceServerId, Guid avatarId, bool isSuccess, int statusCode, string error, byte[] avatarData)
        {
            if (!AvatarIdToAccount.TryGetValue(avatarId, out var avatarAccount))
            {
                DELogger.Warn(
                    nameof(GateServerRuntimeState),
                    $"Received CreateAvatarRsp for unknown avatarId={avatarId}, sourceServerId={sourceServerId}."
                );
                return false;
            }

            DELogger.Info(
                nameof(GateServerRuntimeState),
                $"Received avatar creation result, account={avatarAccount.Account}, clientSessionId={avatarAccount.ClientSessionId}, avatarId={avatarId}, success={isSuccess}, sourceServerId={sourceServerId}."
            );
            return NativeAPI.SendAvatarLoginRsp(avatarAccount.ClientSessionId, avatarId, isSuccess, statusCode, error, avatarData);
        }

        public void Uninitialize()
        {
            AvatarIdToAccount.Clear();
        }

        private bool TrySendLoginRspByAvatarId(Guid avatarId, bool isSuccess, int statusCode, string error)
        {
            if (!AvatarIdToAccount.TryGetValue(avatarId, out var avatarAccount))
            {
                return false;
            }

            return NativeAPI.SendAvatarLoginRsp(avatarAccount.ClientSessionId, avatarId, isSuccess, statusCode, error, Array.Empty<byte>());
        }
    }
}
