using System;
using System.Collections.Generic;
using DE.Server.Entities;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace DE.Server.NativeBridge
{
    public sealed partial class GateServerRuntimeState
    {
        private readonly Dictionary<Guid, GateAvatarMigrationContext> _gateAvatarMigrations = new();

        internal bool HandleAvatarMigrationGateMessage(string sourceServerId, ServerRpcPayload rpc)
        {
            if (!AvatarMigrationProtocol.TryDeserialize(rpc, out var message))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Received malformed Avatar migration Gate message, sourceServerId={sourceServerId ?? "<null>"}, avatarId={rpc.EntityId}, methodId={rpc.MethodId}.");
                return false;
            }

            if (!string.Equals(sourceServerId, message.SourceGameServerId, StringComparison.Ordinal))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Rejected Avatar migration Gate message because source Game does not match the request, avatarId={message.AvatarId}, migrationId={message.MigrationId}, sourceServerId={sourceServerId ?? "<null>"}, expectedSourceGameServerId={message.SourceGameServerId}.");
                return false;
            }

            if (!string.Equals(message.GateServerId, _managedRuntimeState.ServerId, StringComparison.Ordinal))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Rejected Avatar migration Gate message because target Gate does not match the current Gate, avatarId={message.AvatarId}, migrationId={message.MigrationId}, targetGateServerId={message.GateServerId}, currentGateServerId={_managedRuntimeState.ServerId}.");
                return false;
            }

            switch (message.Command)
            {
            case AvatarMigrationCommand.FreezeRoute:
                return HandleFreezeRoute(message);
            case AvatarMigrationCommand.CommitRoute:
                return HandleCommitRoute(message);
            case AvatarMigrationCommand.RollbackRoute:
                return HandleRollbackRoute(message);
            default:
                DELogger.Error(nameof(GateServerRuntimeState), $"Rejected unsupported Avatar migration Gate command, avatarId={message.AvatarId}, migrationId={message.MigrationId}, command={message.Command}.");
                return false;
            }
        }

        internal bool TryQueueClientAvatarRpc(Guid avatarId, byte[] payload)
        {
            return TryQueueAvatarMessage(
                avatarId,
                QueuedAvatarMigrationMessageKind.AvatarRpcToGame,
                string.Empty,
                payload
            );
        }

        internal bool TryQueueAvatarProxyRpc(Guid avatarId, string sourceServerId, byte[] payload)
        {
            return TryQueueAvatarMessage(
                avatarId,
                QueuedAvatarMigrationMessageKind.ServerRpcToGame,
                sourceServerId,
                payload
            );
        }

        internal bool TryQueueServerAvatarRpc(Guid avatarId, string sourceServerId, byte[] payload)
        {
            return TryQueueAvatarMessage(
                avatarId,
                QueuedAvatarMigrationMessageKind.AvatarRpcToClient,
                sourceServerId,
                payload
            );
        }

        internal void ClearGateAvatarImmigrations()
        {
            _gateAvatarMigrations.Clear();
        }

        internal bool IsAvatarMigrationRouteFrozen(Guid avatarId)
        {
            return _gateAvatarMigrations.ContainsKey(avatarId);
        }

        private bool HandleFreezeRoute(AvatarMigrationMessage request)
        {
            if (request == null)
            {
                DELogger.Error(nameof(GateServerRuntimeState), "Failed to freeze Avatar route because migration request is null.");
                return false;
            }

            if (!AvatarIdToAccount.TryGetValue(request.AvatarId, out var account))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Failed to freeze Avatar route because route does not exist, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendGateResponse(request, AvatarMigrationCommand.RouteFrozen, false, "Avatar route does not exist");
            }

            if (account.LastMigrationId == request.MigrationId)
            {
                var command = account.LastMigrationCommitted
                    ? AvatarMigrationCommand.RouteCommitted
                    : AvatarMigrationCommand.RouteRolledBack;
                return SendGateResponse(request, command, true);
            }

            if (_gateAvatarMigrations.TryGetValue(request.AvatarId, out var existingContext))
            {
                var isSameMigration = GameServerRuntimeState.IsSameMigration(existingContext.Message, request);
                if (!isSameMigration)
                {
                    DELogger.Error(nameof(GateServerRuntimeState), $"Failed to freeze Avatar route because another migration is active, avatarId={request.AvatarId}, migrationId={request.MigrationId}, activeMigrationId={existingContext.Message?.MigrationId}.");
                }

                return SendGateResponse(
                    request,
                    AvatarMigrationCommand.RouteFrozen,
                    isSameMigration,
                    isSameMigration ? string.Empty : "another migration is active"
                );
            }

            if (!string.Equals(account.GameServerId, request.SourceGameServerId, StringComparison.Ordinal))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Failed to freeze Avatar route because source Game is not the current route, avatarId={request.AvatarId}, migrationId={request.MigrationId}, sourceGameServerId={request.SourceGameServerId}, currentGameServerId={account.GameServerId}.");
                return SendGateResponse(request, AvatarMigrationCommand.RouteFrozen, false, "source Game is not the current Avatar route");
            }

            _gateAvatarMigrations.Add(
                request.AvatarId,
                new GateAvatarMigrationContext
                {
                    Message = request,
                }
            );
            return SendGateResponse(request, AvatarMigrationCommand.RouteFrozen, true);
        }

        private bool HandleCommitRoute(AvatarMigrationMessage request)
        {
            if (request == null)
            {
                DELogger.Error(nameof(GateServerRuntimeState), "Failed to commit Avatar route because migration request is null.");
                return false;
            }

            if (!AvatarIdToAccount.TryGetValue(request.AvatarId, out var account))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Failed to commit Avatar route because route does not exist, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendGateResponse(request, AvatarMigrationCommand.RouteCommitted, false, "Avatar route does not exist");
            }

            if (account.LastMigrationId == request.MigrationId)
            {
                if (account.LastMigrationCommitted)
                {
                    return SendGateResponse(request, AvatarMigrationCommand.RouteCommitted, true);
                }
            }

            if (!_gateAvatarMigrations.TryGetValue(request.AvatarId, out var context))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Failed to commit Avatar route because route is not frozen, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendGateResponse(request, AvatarMigrationCommand.RouteCommitted, false, "migration route is not frozen");
            }

            if (!GameServerRuntimeState.IsSameMigration(context.Message, request))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Failed to commit Avatar route because request does not match frozen migration, avatarId={request.AvatarId}, migrationId={request.MigrationId}, frozenMigrationId={context.Message?.MigrationId}.");
                return SendGateResponse(request, AvatarMigrationCommand.RouteCommitted, false, "migration request does not match frozen route");
            }

            if (!FlushGateQueue(context, true, account.ClientSessionId))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Failed to commit Avatar route because queued messages could not be flushed, avatarId={request.AvatarId}, migrationId={request.MigrationId}, queuedMessageCount={context.Messages.Count}.");
                return false;
            }

            account.GameServerId = request.TargetGameServerId;
            account.LastMigrationId = request.MigrationId;
            account.LastMigrationCommitted = true;
            _gateAvatarMigrations.Remove(request.AvatarId);
            return SendGateResponse(request, AvatarMigrationCommand.RouteCommitted, true);
        }

        private bool HandleRollbackRoute(AvatarMigrationMessage request)
        {
            if (request == null)
            {
                DELogger.Error(nameof(GateServerRuntimeState), "Failed to roll back Avatar route because migration request is null.");
                return false;
            }

            if (!AvatarIdToAccount.TryGetValue(request.AvatarId, out var account))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Failed to roll back Avatar route because route does not exist, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendGateResponse(request, AvatarMigrationCommand.RouteRolledBack, false, "Avatar route does not exist");
            }

            if (account.LastMigrationId == request.MigrationId)
            {
                if (!account.LastMigrationCommitted)
                {
                    return SendGateResponse(request, AvatarMigrationCommand.RouteRolledBack, true);
                }
            }

            if (!_gateAvatarMigrations.TryGetValue(request.AvatarId, out var context))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Failed to roll back Avatar route because route is not frozen, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendGateResponse(request, AvatarMigrationCommand.RouteRolledBack, false, "migration route is not frozen");
            }

            if (!GameServerRuntimeState.IsSameMigration(context.Message, request))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Failed to roll back Avatar route because request does not match frozen migration, avatarId={request.AvatarId}, migrationId={request.MigrationId}, frozenMigrationId={context.Message?.MigrationId}.");
                return SendGateResponse(request, AvatarMigrationCommand.RouteRolledBack, false, "migration request does not match frozen route");
            }

            if (!FlushGateQueue(context, false, account.ClientSessionId))
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Failed to roll back Avatar route because queued messages could not be flushed, avatarId={request.AvatarId}, migrationId={request.MigrationId}, queuedMessageCount={context.Messages.Count}.");
                return false;
            }

            account.GameServerId = request.SourceGameServerId;
            account.LastMigrationId = request.MigrationId;
            account.LastMigrationCommitted = false;
            _gateAvatarMigrations.Remove(request.AvatarId);
            return SendGateResponse(request, AvatarMigrationCommand.RouteRolledBack, true);
        }

        private bool TryQueueAvatarMessage(
            Guid avatarId,
            QueuedAvatarMigrationMessageKind kind,
            string sourceServerId,
            byte[] payload)
        {
            if (!_gateAvatarMigrations.TryGetValue(avatarId, out var context))
            {
                return false;
            }

            context.Messages.Enqueue(
                new QueuedAvatarMigrationMessage
                {
                    Kind = kind,
                    SourceServerId = sourceServerId ?? string.Empty,
                    Payload = payload == null ? Array.Empty<byte>() : (byte[])payload.Clone(),
                }
            );
            return true;
        }

        private bool FlushGateQueue(GateAvatarMigrationContext context, bool commit, ulong clientSessionId)
        {
            if (context == null)
            {
                DELogger.Error(nameof(GateServerRuntimeState), "Failed to flush Avatar migration queue because migration context is null.");
                return false;
            }

            while (context.Messages.Count > 0)
            {
                var message = context.Messages.Peek();
                if (message == null)
                {
                    DELogger.Error(nameof(GateServerRuntimeState), $"Failed to flush Avatar migration queue because queued message is null, avatarId={context.Message?.AvatarId}, migrationId={context.Message?.MigrationId}.");
                    return false;
                }

                bool sent;
                switch (message.Kind)
                {
                case QueuedAvatarMigrationMessageKind.AvatarRpcToGame:
                    sent = NativeAPI.SendAvatarRpcToServer(
                        commit ? context.Message.TargetGameServerId : context.Message.SourceGameServerId,
                        message.Payload
                    );
                    break;
                case QueuedAvatarMigrationMessageKind.ServerRpcToGame:
                    sent = NativeAPI.SendServerRpcToServer(
                        commit ? context.Message.TargetGameServerId : context.Message.SourceGameServerId,
                        message.Payload
                    );
                    break;
                case QueuedAvatarMigrationMessageKind.AvatarRpcToClient:
                    if (clientSessionId == 0)
                    {
                        DELogger.Error(nameof(GateServerRuntimeState), $"Discarded queued Avatar RPC to Client because client session is unavailable, avatarId={context.Message.AvatarId}, migrationId={context.Message.MigrationId}.");
                        context.Messages.Dequeue();
                        continue;
                    }

                    if (!commit)
                    {
                        if (!string.Equals(message.SourceServerId, context.Message.SourceGameServerId, StringComparison.Ordinal))
                        {
                            DELogger.Error(nameof(GateServerRuntimeState), $"Discarded queued Avatar RPC to Client during rollback because it originated from target Game, avatarId={context.Message.AvatarId}, migrationId={context.Message.MigrationId}, messageSourceServerId={message.SourceServerId}, sourceGameServerId={context.Message.SourceGameServerId}.");
                            context.Messages.Dequeue();
                            continue;
                        }
                    }

                    sent = NativeAPI.SendAvatarRpcToClient(clientSessionId, message.Payload);
                    break;
                default:
                    DELogger.Error(nameof(GateServerRuntimeState), $"Failed to flush Avatar migration queue because message kind is unsupported, avatarId={context.Message?.AvatarId}, migrationId={context.Message?.MigrationId}, messageKind={message.Kind}.");
                    return false;
                }

                if (!sent)
                {
                    DELogger.Error(nameof(GateServerRuntimeState), $"Failed to send queued Avatar migration message, avatarId={context.Message.AvatarId}, migrationId={context.Message.MigrationId}, messageKind={message.Kind}, commit={commit}.");
                    return false;
                }

                context.Messages.Dequeue();
            }

            return true;
        }

        private bool SendGateResponse(
            AvatarMigrationMessage request,
            AvatarMigrationCommand command,
            bool success,
            string error = null)
        {
            var response = request.CreateResponse(command, success, error);
            var payload = AvatarMigrationProtocol.BuildServerRpcPayload(
                ServerRpcTargetKind.AvatarMigrationGame,
                request.SourceGameServerId,
                response
            );
            var sent = NativeAPI.SendServerRpcToServer(request.SourceGameServerId, payload);
            if (!sent)
            {
                DELogger.Error(nameof(GateServerRuntimeState), $"Failed to send Avatar migration Gate response, avatarId={request.AvatarId}, migrationId={request.MigrationId}, command={command}, success={success}, sourceGameServerId={request.SourceGameServerId}.");
            }

            return sent;
        }
    }
}
