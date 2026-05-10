using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using DE.Server.Auth;
using DE.Server.Entities;

namespace DE.Server.NativeBridge
{
    public sealed class AvatarAccount
    {
        public Guid AvatarId { get; set; }
        public string Account { get; set; } = string.Empty;
        public ulong ClientSessionId { get; set; }
        public string GameServerId { get; set; } = string.Empty;
        public bool CreateAvatarPending { get; set; }
    }

    public sealed class GateServerRuntimeState
    {
        private const int ReplacedByNewLoginStatusCode = 409;

        private readonly ManagedRuntimeState _managedRuntimeState;

        public GateServerRuntimeState(ManagedRuntimeState managedRuntimeState)
        {
            _managedRuntimeState = managedRuntimeState ?? throw new ArgumentNullException(nameof(managedRuntimeState));
        }

        public Dictionary<Guid, AvatarAccount> AvatarIdToAccount { get; } = new Dictionary<Guid, AvatarAccount>();
        public Dictionary<ulong, Guid> ClientSessionIdToAvatarId { get; } = new Dictionary<ulong, Guid>();
        public Dictionary<string, Guid> AccountToAvatarId { get; } = new Dictionary<string, Guid>(StringComparer.Ordinal);

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
            var avatarId = GenerateStableAvatarId(accountName);

            var createAvatarPending = false;
            if (AccountToAvatarId.TryGetValue(accountName, out var existedAvatarId)
                && AvatarIdToAccount.TryGetValue(existedAvatarId, out var existedAccount))
            {
                createAvatarPending = existedAccount.CreateAvatarPending;
                if (existedAccount.ClientSessionId != clientSessionId)
                {
                    if (!existedAccount.CreateAvatarPending)
                    {
                        NotifyAvatarClientDetached(existedAccount, AvatarClientDetachReason.ReplacedByNewLogin);
                    }

                    NativeAPI.SendAvatarLoginRsp(
                        existedAccount.ClientSessionId,
                        avatarId,
                        false,
                        ReplacedByNewLoginStatusCode,
                        "account logged in from another client",
                        Array.Empty<byte>()
                    );
                    ClientSessionIdToAvatarId.Remove(existedAccount.ClientSessionId);
                    NativeAPI.ActiveDisconnectClient(existedAccount.ClientSessionId);
                    DELogger.Info(
                        nameof(GateServerRuntimeState),
                        $"Kicked previous login session, account={accountName}, oldClientSessionId={existedAccount.ClientSessionId}, newClientSessionId={clientSessionId}, avatarId={avatarId}."
                    );
                }
            }

            var avatarAccount = new AvatarAccount
            {
                AvatarId = avatarId,
                Account = accountName,
                ClientSessionId = clientSessionId,
                CreateAvatarPending = createAvatarPending,
            };
            AvatarIdToAccount[avatarId] = avatarAccount;
            AccountToAvatarId[accountName] = avatarId;
            ClientSessionIdToAvatarId[clientSessionId] = avatarId;

            DELogger.Info(
                nameof(GateServerRuntimeState),
                $"Avatar login request accepted, account={accountName}, clientSessionId={clientSessionId}, avatarId={avatarId}."
            );

            if (avatarAccount.CreateAvatarPending)
            {
                DELogger.Info(
                    nameof(GateServerRuntimeState),
                    $"Rebound account to new client session while avatar creation is pending, account={accountName}, clientSessionId={clientSessionId}, avatarId={avatarId}."
                );
                return CreateAvatarRemote(avatarId);
            }

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
                var sent = TrySendLoginRspByAvatarId(avatarId, false, 503, "no game server is available");
                if (AvatarIdToAccount.TryGetValue(avatarId, out var failedAvatarAccount))
                {
                    ClearAvatarAccount(failedAvatarAccount);
                }

                return sent;
            }

            if (AvatarIdToAccount.TryGetValue(avatarId, out var avatarAccount))
            {
                avatarAccount.GameServerId = gameServerId;
            }

            var createAvatarReqSent = NativeAPI.SendCreateAvatarReq(gameServerId, avatarId, avatarAccount == null ? 0 : avatarAccount.ClientSessionId);
            if (!createAvatarReqSent)
            {
                var loginRspSent = TrySendLoginRspByAvatarId(avatarId, false, 503, "failed to send create avatar request");
                if (avatarAccount != null)
                {
                    ClearAvatarAccount(avatarAccount);
                }

                return loginRspSent;
            }

            if (avatarAccount != null)
            {
                avatarAccount.CreateAvatarPending = true;
            }

            DELogger.Info(
                nameof(GateServerRuntimeState),
                $"Requested avatar creation, avatarId={avatarId}, gameServerId={gameServerId}."
            );
            return true;
        }

        public bool HandleCreateAvatarRsp(string sourceServerId, Guid avatarId, ulong clientSessionId, bool isSuccess, int statusCode, string error, byte[] avatarData)
        {
            if (!AvatarIdToAccount.TryGetValue(avatarId, out var avatarAccount))
            {
                DELogger.Warn(
                    nameof(GateServerRuntimeState),
                    $"Received CreateAvatarRsp for unknown avatarId={avatarId}, sourceServerId={sourceServerId}."
                );
                return false;
            }

            if (clientSessionId != 0 && avatarAccount.ClientSessionId != clientSessionId)
            {
                DELogger.Info(
                    nameof(GateServerRuntimeState),
                    $"Ignored stale CreateAvatarRsp, account={avatarAccount.Account}, avatarId={avatarId}, responseClientSessionId={clientSessionId}, currentClientSessionId={avatarAccount.ClientSessionId}, sourceServerId={sourceServerId}."
                );
                return true;
            }

            avatarAccount.CreateAvatarPending = false;

            DELogger.Info(
                nameof(GateServerRuntimeState),
                $"Received avatar creation result, account={avatarAccount.Account}, clientSessionId={avatarAccount.ClientSessionId}, avatarId={avatarId}, success={isSuccess}, sourceServerId={sourceServerId}."
            );
            var sent = NativeAPI.SendAvatarLoginRsp(avatarAccount.ClientSessionId, avatarId, isSuccess, statusCode, error, avatarData);
            if (!isSuccess)
            {
                ClearAvatarAccount(avatarAccount);
            }

            return sent;
        }

        public bool HandleClientDisconnect(ulong clientSessionId)
        {
            if (!ClientSessionIdToAvatarId.TryGetValue(clientSessionId, out var avatarId))
            {
                return true;
            }

            ClientSessionIdToAvatarId.Remove(clientSessionId);
            if (!AvatarIdToAccount.TryGetValue(avatarId, out var avatarAccount)
                || avatarAccount.ClientSessionId != clientSessionId)
            {
                return true;
            }

            if (!avatarAccount.CreateAvatarPending)
            {
                NotifyAvatarClientDetached(avatarAccount, AvatarClientDetachReason.Disconnected);
            }

            ClearAvatarAccount(avatarAccount);

            DELogger.Info(
                nameof(GateServerRuntimeState),
                $"Cleared avatar login session, account={avatarAccount.Account}, clientSessionId={clientSessionId}, avatarId={avatarId}."
            );
            return true;
        }

        public bool HandleClientAvatarRpc(ulong clientSessionId, byte[] payload)
        {
            if (!ClientSessionIdToAvatarId.TryGetValue(clientSessionId, out var avatarId)
                || !AvatarIdToAccount.TryGetValue(avatarId, out var avatarAccount))
            {
                DELogger.Warn(
                    nameof(GateServerRuntimeState),
                    $"Received avatar RPC for unauthenticated clientSessionId={clientSessionId}."
                );
                return false;
            }

            if (string.IsNullOrWhiteSpace(avatarAccount.GameServerId))
            {
                DELogger.Warn(
                    nameof(GateServerRuntimeState),
                    $"Received avatar RPC before game route is available, avatarId={avatarId}, clientSessionId={clientSessionId}."
                );
                return false;
            }

            var managedPayload = EnsureAvatarIdInPayload(avatarId, payload);
            return NativeAPI.SendAvatarRpcToServer(avatarAccount.GameServerId, managedPayload);
        }

        public bool HandleServerAvatarRpc(string sourceServerId, byte[] payload)
        {
            if (!TryReadAvatarIdFromPayload(payload, out var avatarId)
                || !AvatarIdToAccount.TryGetValue(avatarId, out var avatarAccount))
            {
                DELogger.Warn(
                    nameof(GateServerRuntimeState),
                    $"Received server avatar RPC for unknown avatarId={avatarId}, sourceServerId={sourceServerId}."
                );
                return false;
            }

            return NativeAPI.SendAvatarRpcToClient(avatarAccount.ClientSessionId, payload);
        }

        public bool HandleServerRpc(string sourceServerId, byte[] payload)
        {
            if (!ServerRpcPayload.TryDeserialize(payload, 0, payload == null ? 0 : payload.Length, out var serverRpc))
            {
                DELogger.Warn(nameof(GateServerRuntimeState), $"Received invalid server RPC payload, sourceServerId={sourceServerId}.");
                return false;
            }

            return HandleServerRpcRelay(sourceServerId, serverRpc, payload);
        }

        public void Uninitialize()
        {
            AvatarIdToAccount.Clear();
            ClientSessionIdToAvatarId.Clear();
            AccountToAvatarId.Clear();
        }

        private bool TrySendLoginRspByAvatarId(Guid avatarId, bool isSuccess, int statusCode, string error)
        {
            if (!AvatarIdToAccount.TryGetValue(avatarId, out var avatarAccount))
            {
                return false;
            }

            return NativeAPI.SendAvatarLoginRsp(avatarAccount.ClientSessionId, avatarId, isSuccess, statusCode, error, Array.Empty<byte>());
        }

        private void ClearAvatarAccount(AvatarAccount avatarAccount)
        {
            if (avatarAccount == null)
            {
                return;
            }

            AvatarIdToAccount.Remove(avatarAccount.AvatarId);
            ClientSessionIdToAvatarId.Remove(avatarAccount.ClientSessionId);
            if (!string.IsNullOrWhiteSpace(avatarAccount.Account)
                && AccountToAvatarId.TryGetValue(avatarAccount.Account, out var avatarId)
                && avatarId == avatarAccount.AvatarId)
            {
                AccountToAvatarId.Remove(avatarAccount.Account);
            }
        }

        private static bool NotifyAvatarClientDetached(AvatarAccount avatarAccount, AvatarClientDetachReason reason)
        {
            if (avatarAccount == null || string.IsNullOrWhiteSpace(avatarAccount.GameServerId))
            {
                return false;
            }

            var payload = RpcCaller.BuildServerRpcPayload(
                ServerRpcTargetKind.AvatarProxy,
                avatarAccount.AvatarId,
                avatarAccount.GameServerId,
                string.Empty,
                "OnAvatarClientDetached",
                new object[] { avatarAccount.ClientSessionId, reason }
            );
            return NativeAPI.SendServerRpcToServer(avatarAccount.GameServerId, payload);
        }

        private bool HandleServerRpcRelay(string sourceServerId, ServerRpcPayload serverRpc, byte[] payload)
        {
            if (serverRpc.TargetKind == ServerRpcTargetKind.Stub || serverRpc.TargetKind == ServerRpcTargetKind.Entity)
            {
                if (string.IsNullOrWhiteSpace(serverRpc.TargetServerId))
                {
                    DELogger.Warn(
                        nameof(GateServerRuntimeState),
                        $"Received server RPC without target server id, targetKind={serverRpc.TargetKind}, stubName={serverRpc.StubName}, entityId={serverRpc.EntityId}, sourceServerId={sourceServerId}."
                    );
                    return false;
                }

                return NativeAPI.SendServerRpcToServer(serverRpc.TargetServerId, payload);
            }

            if (serverRpc.TargetKind != ServerRpcTargetKind.AvatarProxy)
            {
                DELogger.Warn(
                    nameof(GateServerRuntimeState),
                    $"Received unsupported server RPC target kind {serverRpc.TargetKind} on gate, sourceServerId={sourceServerId}."
                );
                return false;
            }

            if (!AvatarIdToAccount.TryGetValue(serverRpc.EntityId, out var avatarAccount)
                || string.IsNullOrWhiteSpace(avatarAccount.GameServerId))
            {
                DELogger.Warn(
                    nameof(GateServerRuntimeState),
                    $"Received avatar proxy RPC for unknown avatar route, avatarId={serverRpc.EntityId}, sourceServerId={sourceServerId}."
                );
                return false;
            }

            return NativeAPI.SendServerRpcToServer(avatarAccount.GameServerId, payload);
        }

        private static byte[] EnsureAvatarIdInPayload(Guid avatarId, byte[] payload)
        {
            if (payload == null)
            {
                return Array.Empty<byte>();
            }

            var copiedPayload = new byte[payload.Length];
            Buffer.BlockCopy(payload, 0, copiedPayload, 0, payload.Length);
            if (copiedPayload.Length >= 20)
            {
                var avatarBytes = avatarId.ToByteArray();
                Buffer.BlockCopy(avatarBytes, 0, copiedPayload, 4, avatarBytes.Length);
            }

            return copiedPayload;
        }

        private static bool TryReadAvatarIdFromPayload(byte[] payload, out Guid avatarId)
        {
            avatarId = Guid.Empty;
            if (payload == null || payload.Length < 20)
            {
                return false;
            }

            var avatarBytes = new byte[16];
            Buffer.BlockCopy(payload, 4, avatarBytes, 0, avatarBytes.Length);
            avatarId = new Guid(avatarBytes);
            return avatarId != Guid.Empty;
        }

        private static Guid GenerateStableAvatarId(string account)
        {
            var normalizedAccount = account == null ? string.Empty : account.Trim();
            var source = Encoding.UTF8.GetBytes("DEGF:Avatar:" + normalizedAccount);
            var hash = SHA256.HashData(source);
            var bytes = new byte[16];
            Buffer.BlockCopy(hash, 0, bytes, 0, bytes.Length);
            bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes);
        }
    }
}
