using System;
using System.Collections.Generic;
using System.Linq;
using DE.Server.Entities;

namespace DE.Server.NativeBridge
{
    public sealed partial class GateServerRuntimeState
    {
        private readonly Dictionary<Guid, GateAvatarMigrationContext> _gateAvatarMigrations = new();
        private readonly Dictionary<Guid, AvatarMigrationMessage> _completedGateAvatarMigrations = new();
        // KBEngine Entity::pBufferedSendToClientMessages_ exists only when End preceded Start.
        private readonly HashSet<Guid> _gateAvatarClientForwarders = new();

        internal bool HandleAvatarMigrationGateMessage(string sourceServerId, SystemCommandPayload commandEnvelope)
        {
            if (!AvatarMigrationProtocol.TryDeserialize(commandEnvelope, out var request)
                || (request.Command != AvatarMigrationCommand.MigrationStart && request.Command != AvatarMigrationCommand.MigrationEnd
                    && request.Command != AvatarMigrationCommand.SourceInputDrained)
                || (request.Command == AvatarMigrationCommand.SourceInputDrained && !request.Success))
            {
                DELogger.Error(nameof(GateServerRuntimeState), "Received malformed or unsupported Avatar migration Gate message.");
                return false;
            }

            var expectedSender = request.Command == AvatarMigrationCommand.MigrationEnd && request.Success
                ? request.TargetGameServerId : request.SourceGameServerId;
            if (!string.Equals(sourceServerId, expectedSender, StringComparison.Ordinal)
                || !string.Equals(request.GateServerId, _managedRuntimeState.ServerId, StringComparison.Ordinal)
                || !string.Equals(commandEnvelope.TargetServerId, _managedRuntimeState.ServerId, StringComparison.Ordinal)
                || !_managedRuntimeState.GetGameServerIds().Contains(request.SourceGameServerId)
                || !_managedRuntimeState.GetGameServerIds().Contains(request.TargetGameServerId))
            {
                DELogger.Error(nameof(GateServerRuntimeState),
                    $"Rejected Avatar migration association, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return false;
            }

            // Completed identities are retained so delayed commands cannot resurrect a route after A -> B -> A.
            if (_completedGateAvatarMigrations.TryGetValue(request.MigrationId, out var completed))
            {
                return GameServerRuntimeState.IsSameMigration(completed, request)
                    && (request.Command == AvatarMigrationCommand.MigrationStart || completed.Success == request.Success);
            }

            if (!AvatarIdToAccount.TryGetValue(request.AvatarId, out var account)
                || !string.Equals(account.GameServerId, request.SourceGameServerId, StringComparison.Ordinal)
                || (account.ClientSessionId != 0 && account.ClientSessionId != request.ClientSessionId))
            {
                DELogger.Error(nameof(GateServerRuntimeState),
                    $"Rejected Avatar migration route or session, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return false;
            }

            if (_gateAvatarMigrations.TryGetValue(request.AvatarId, out var context))
            {
                if (!GameServerRuntimeState.IsSameMigration(context.Message, request))
                {
                    return false;
                }
            }
            else
            {
                // A drain receipt may complete an existing handoff, but cannot start one.
                if (request.Command == AvatarMigrationCommand.SourceInputDrained)
                {
                    return false;
                }
                context = new GateAvatarMigrationContext
                {
                    Message = request,
                    Flags = request.Command == AvatarMigrationCommand.MigrationStart
                        ? GateAvatarMigrationFlags.TeleportStart : GateAvatarMigrationFlags.TeleportStop,
                };
                _gateAvatarMigrations.Add(request.AvatarId, context);
                if (request.Command == AvatarMigrationCommand.MigrationEnd)
                {
                    _gateAvatarClientForwarders.Add(request.AvatarId);
                }

                return true;
            }

            switch (request.Command)
            {
            case AvatarMigrationCommand.MigrationStart:
                return HandleMigrationStart(context, account);
            case AvatarMigrationCommand.MigrationEnd:
                return HandleMigrationEnd(context, account, request);
            case AvatarMigrationCommand.SourceInputDrained:
                if (context.Message.Command == AvatarMigrationCommand.MigrationEnd && !context.Message.Success)
                {
                    return false;
                }
                context.SourceInputDrained = true;
                return CompleteGateMigration(context, account);
            default:
                return false;
            }
        }

        internal bool IsAvatarMigrating(Guid avatarId)
        {
            return _gateAvatarMigrations.ContainsKey(avatarId);
        }

        private bool TryBufferAvatarClientInput(Guid avatarId, byte[] payload)
        {
            if (!_gateAvatarMigrations.TryGetValue(avatarId, out var context))
            {
                return false;
            }

            // HandleClientAvatarRpc owns this freshly serialized payload. Ghost forwards use a separate
            // relay entry and must keep flowing so they precede the source's drain receipt.
            context.ClientInputs.Enqueue(payload);
            return true;
        }

        internal bool TryBufferAvatarClientRpc(Guid avatarId, string sourceServerId, byte[] payload)
        {
            if (!_gateAvatarMigrations.TryGetValue(avatarId, out var context)
                || !_gateAvatarClientForwarders.Contains(avatarId))
            {
                return false;
            }

            var outputGame = context.Message.Success ? context.Message.TargetGameServerId : context.Message.SourceGameServerId;
            if (!string.Equals(sourceServerId, outputGame, StringComparison.Ordinal))
            {
                // The old source channel may still deliver output preceding MigrationStart; it must pass through first.
                return false;
            }

            if (AvatarIdToAccount.TryGetValue(avatarId, out var account) && account.ClientSessionId == 0)
            {
                DELogger.Info(nameof(GateServerRuntimeState),
                    $"Discarded migration output for a disconnected client, avatarId={avatarId}, migrationId={context.Message.MigrationId}.");
                return true;
            }

            // KBEngine 2.5.12 BaseMessagesForwardClientHandler::pushMessages: only End-before-Start buffers client output.
            context.ClientMessages.Enqueue(payload == null ? Array.Empty<byte>() : (byte[])payload.Clone());
            return true;
        }

        internal void ClearGateAvatarImmigrations()
        {
            _gateAvatarMigrations.Clear();
            _completedGateAvatarMigrations.Clear();
            _gateAvatarClientForwarders.Clear();
        }

        private bool HandleMigrationStart(GateAvatarMigrationContext context, AvatarAccount account)
        {
            // KBEngine 2.5.12 Entity::onMigrationCellappStart: meet TELEPORT_STOP or record TELEPORT_START, without an ACK.
            if (context.Message.Command == AvatarMigrationCommand.MigrationEnd)
            {
                context.Flags &= ~GateAvatarMigrationFlags.TeleportStop;
                return CompleteGateMigration(context, account);
            }

            context.Flags |= GateAvatarMigrationFlags.TeleportStart;
            return true;
        }

        private bool HandleMigrationEnd(GateAvatarMigrationContext context, AvatarAccount account, AvatarMigrationMessage request)
        {
            if (context.Message.Command == AvatarMigrationCommand.MigrationEnd && context.Message.Success != request.Success)
            {
                DELogger.Error(nameof(GateServerRuntimeState),
                    $"Rejected contradictory migration result, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return false;
            }

            context.Message = request;
            // KBEngine 2.5.12 Entity::onMigrationCellappEnd: Start already arrived, or create the client forwarding handler.
            if ((context.Flags & GateAvatarMigrationFlags.TeleportStart) != 0)
            {
                context.Flags &= ~GateAvatarMigrationFlags.TeleportStart;
                return CompleteGateMigration(context, account);
            }

            // A duplicate End either still awaits Start, or repeats an unsuccessful forwarding/send operation.
            return (context.Flags & GateAvatarMigrationFlags.TeleportStop) != 0 || CompleteGateMigration(context, account);
        }

        private bool CompleteGateMigration(GateAvatarMigrationContext context, AvatarAccount account)
        {
            // Keep the KBE rendezvous; only successful handoffs additionally wait for source input drain.
            if (context.Message.Command != AvatarMigrationCommand.MigrationEnd || context.Flags != GateAvatarMigrationFlags.None
                || (context.Message.Success && !context.SourceInputDrained))
            {
                return true;
            }

            // KBEngine BaseMessagesForwardClientHandler::process / Entity::onBufferedForwardToClientMessagesOver:
            // drain the earlier target output before onMigrationCellappOver changes the associated Cell/Game.
            while (context.ClientMessages.Count > 0)
            {
                if (account.ClientSessionId == 0)
                {
                    DELogger.Info(nameof(GateServerRuntimeState),
                        $"Discarded queued migration output for a disconnected client, avatarId={context.Message.AvatarId}.");
                    context.ClientMessages.Dequeue();
                    continue;
                }

                if (!NativeAPI.SendAvatarRpcToClient(account.ClientSessionId, context.ClientMessages.Peek()))
                {
                    DELogger.Error(nameof(GateServerRuntimeState),
                        $"Failed to forward migration output, avatarId={context.Message.AvatarId}, migrationId={context.Message.MigrationId}.");
                    return false;
                }

                context.ClientMessages.Dequeue();
            }

            var request = context.Message;
            var targetGame = request.Success ? request.TargetGameServerId : request.SourceGameServerId;
            var response = request.CreateResponse(AvatarMigrationCommand.MigrationOver, request.Success, request.Error);
            var payload = AvatarMigrationProtocol.BuildSystemCommandPayload(targetGame, response);
            // KBEngine 2.5.12 Entity::onMigrationCellappOver sends reqTeleportToCellAppOver before replacing cellEntityCall's ID.
            if (!NativeAPI.SendSystemCommandToServer(targetGame, payload))
            {
                DELogger.Error(nameof(GateServerRuntimeState),
                    $"Failed to send MigrationOver, avatarId={request.AvatarId}, migrationId={request.MigrationId}, targetGame={targetGame}.");
                return false;
            }

            // All old Ghost forwards were sent on this same Gate -> target session before the drain
            // receipt was processed. Send the FIFO before removing the context and resuming direct input.
            while (context.ClientInputs.Count > 0)
            {
                if (!NativeAPI.SendServerRpcToServer(targetGame, context.ClientInputs.Peek()))
                {
                    DELogger.Error(nameof(GateServerRuntimeState),
                        $"Failed to forward migration input, avatarId={request.AvatarId}, migrationId={request.MigrationId}, targetGame={targetGame}.");
                    return false;
                }

                context.ClientInputs.Dequeue();
            }

            account.GameServerId = targetGame;
            account.LastMigrationId = request.MigrationId;
            account.LastMigrationCommitted = request.Success;
            _completedGateAvatarMigrations.Add(request.MigrationId, request);
            _gateAvatarClientForwarders.Remove(request.AvatarId);
            _gateAvatarMigrations.Remove(request.AvatarId);
            return true;
        }
    }
}
