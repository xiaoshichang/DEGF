using System;
using System.Collections.Generic;
using System.Linq;
using DE.Server.Entities;
using DE.Share.Rpc;

namespace DE.Server.NativeBridge
{
    public sealed partial class GameServerRuntimeState
    {
        // Synchronous call protection only; this is not an additional distributed migration state.
        private readonly HashSet<ServerAvatarEntity> _avatarTeleportCalls = new();

        public TeleportErrorCode Teleport(ServerAvatarEntity avatar, EntityMailBox space)
        {
            if (avatar == null)
            {
                return TeleportErrorCode.AvatarIsNull;
            }
            if (!_avatarTeleportCalls.Add(avatar))
            {
                return TeleportErrorCode.ImmigrationInProgress;
            }
            try
            {
                return TeleportCore(avatar, space);
            }
            finally
            {
                _avatarTeleportCalls.Remove(avatar);
            }
        }

        private TeleportErrorCode TeleportCore(ServerAvatarEntity avatar, EntityMailBox space)
        {
            if (avatar == null)
            {
                return TeleportErrorCode.AvatarIsNull;
            }

            if (avatar.Guid == Guid.Empty)
            {
                return TeleportErrorCode.AvatarIdIsEmpty;
            }

            if (!space.IsValid)
            {
                return TeleportErrorCode.InvalidTargetSpaceMailBox;
            }

            if (!Avatars.TryGetValue(avatar.Guid, out var registeredAvatar))
            {
                return TeleportErrorCode.AvatarNotRegistered;
            }

            if (!ReferenceEquals(registeredAvatar, avatar))
            {
                return TeleportErrorCode.AvatarInstanceMismatch;
            }

            // Keep the source context through leave/restore callbacks, before Ghost is observable.
            if (_sourceAvatarMigrations.Values.Any(context => ReferenceEquals(context.Avatar, avatar)))
            {
                return TeleportErrorCode.ImmigrationInProgress;
            }

            if (!string.Equals(space.BindingGame, _managedRuntimeState.ServerId, StringComparison.Ordinal))
            {
                return BeginAvatarImmigration(avatar, space);
            }

            if (avatar.ImmigateCmpt.ImmigrationState != AvatarImmigrationState.Real)
            {
                return TeleportErrorCode.ImmigrationInProgress;
            }

            if (!Entities.TryGetValue(space.EntityId, out var targetEntity))
            {
                return TeleportErrorCode.TargetSpaceNotFound;
            }

            if (targetEntity is not ServerSpaceEntity targetSpace)
            {
                return TeleportErrorCode.TargetEntityIsNotSpace;
            }

            if (ReferenceEquals(avatar.SpaceCmpt.CurrentSpace, targetSpace))
            {
                return TeleportErrorCode.AlreadyInTargetSpace;
            }

            if (targetSpace.MaxPlayers > 0 && targetSpace.AvatarManageCmpt.Avatars.Count >= targetSpace.MaxPlayers)
            {
                return TeleportErrorCode.TargetSpaceFull;
            }
            var sourceSpace = avatar.SpaceCmpt.CurrentSpace;
            if (sourceSpace != null)
            {
                if (!sourceSpace.AvatarManageCmpt.Leave(avatar))
                {
                    return TeleportErrorCode.LeaveSourceSpaceFailed;
                }
            }

            if (!targetSpace.AvatarManageCmpt.CanEnter(avatar))
            {
                if (sourceSpace != null)
                {
                    if (!sourceSpace.AvatarManageCmpt.CanEnter(avatar))
                    {
                        return TeleportErrorCode.RestoreSourceSpaceFailed;
                    }
                    sourceSpace.AvatarManageCmpt.Enter(avatar);
                }
                return TeleportErrorCode.EnterTargetSpaceFailed;
            }
            targetSpace.AvatarManageCmpt.Enter(avatar);
            return TeleportErrorCode.Success;
        }

        private readonly Dictionary<Guid, SourceAvatarMigrationContext> _sourceAvatarMigrations = new();
        private readonly Dictionary<Guid, TargetAvatarMigrationContext> _targetAvatarMigrations = new();
        private readonly Dictionary<Guid, AvatarMigrationMessage> _lastTargetTeleportResults = new();
        private readonly Dictionary<Guid, AvatarForwardingRoute> _avatarForwardingRoutes = new();

        private TeleportErrorCode BeginAvatarImmigration(ServerAvatarEntity avatar, EntityMailBox targetSpace)
        {
            if (avatar.ImmigateCmpt.ImmigrationState != AvatarImmigrationState.Real)
            {
                return TeleportErrorCode.ImmigrationInProgress;
            }
            if (!avatar.IsAllowImmigrate())
            {
                return TeleportErrorCode.ImmigrationNotAllowed;
            }
            if (!avatar.Proxy.IsValid)
            {
                return TeleportErrorCode.InvalidAvatarProxy;
            }
            if (!_managedRuntimeState.GetGameServerIds().Contains(targetSpace.BindingGame))
            {
                return TeleportErrorCode.InvalidTargetSpaceMailBox;
            }

            RemoveExpiredAvatarForwardingRoutes();
            var message = new AvatarMigrationMessage
            {
                Command = AvatarMigrationCommand.MigrationStart,
                MigrationId = Guid.NewGuid(),
                AvatarId = avatar.Guid,
                TargetSpaceId = targetSpace.EntityId,
                SourceGameServerId = _managedRuntimeState.ServerId,
                TargetGameServerId = targetSpace.BindingGame,
                GateServerId = avatar.Proxy.BindingGate,
                ClientSessionId = AvatarClientSessionIds.TryGetValue(avatar.Guid, out var sessionId) ? sessionId : 0,
            };
            var context = new SourceAvatarMigrationContext
            {
                Avatar = avatar,
                Message = message,
                SourceSpaceId = avatar.SpaceCmpt.CurrentSpace?.Guid ?? Guid.Empty,
            };
            // KBE Entity::teleportRefEntityCall notifies Base without waiting for a freeze acknowledgement.
            if (!SendMigrationMessage(message.GateServerId, message))
            {
                return TeleportErrorCode.MigrationStartRequestFailed;
            }
            _sourceAvatarMigrations.Add(message.MigrationId, context);
            var failureCode = TeleportErrorCode.MigrationSnapshotFailed;
            string failure;
            try
            {
                avatar.SynchronizeProperties(this);
                var sourceSpace = avatar.SpaceCmpt.CurrentSpace;
                if (sourceSpace != null && !sourceSpace.AvatarManageCmpt.Leave(avatar))
                {
                    failureCode = TeleportErrorCode.LeaveSourceSpaceFailed;
                    throw new InvalidOperationException("Failed to leave the source Space");
                }
                avatar.FreezePropertyWrites();
                avatar.SynchronizeProperties(this, true);
                context.AvatarData = EntitySerializer.SerializeSnapshot(avatar, EntitySerializationProfile.Migration);

                // KBE changeToGhost: retain the source object, but never execute its incoming RPC locally.
                avatar.ImmigateCmpt.ChangeToGhost(message.MigrationId, targetSpace);
                _avatarForwardingRoutes[avatar.Guid] = new AvatarForwardingRoute
                {
                    Message = message,
                    ExpiresAtUtc = DateTime.MaxValue,
                };
                var transfer = message.CreateResponse(AvatarMigrationCommand.TeleportToGame);
                transfer.AvatarData = context.AvatarData;
                if (!SendMigrationMessage(message.TargetGameServerId, transfer))
                {
                    failureCode = TeleportErrorCode.TransferRequestFailed;
                    throw new InvalidOperationException("Failed to send the Avatar transfer");
                }
                return TeleportErrorCode.Success;
            }
            catch (Exception exception)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar transfer failed: {exception}");
                failure = exception.Message;
            }
            RestoreSourceAvatar(context, failure);
            return failureCode;
        }

        internal bool HandleAvatarMigrationGameMessage(string sourceServerId, SystemCommandPayload envelope)
        {
            if (!AvatarMigrationProtocol.TryDeserialize(envelope, out var message)
                || sourceServerId != message.GateServerId || envelope.TargetServerId != _managedRuntimeState.ServerId
                || !_managedRuntimeState.GetGateServerIds().Contains(message.GateServerId)
                || !_managedRuntimeState.GetGameServerIds().Contains(message.SourceGameServerId)
                || !_managedRuntimeState.GetGameServerIds().Contains(message.TargetGameServerId))
            {
                return false;
            }
            switch (message.Command)
            {
            case AvatarMigrationCommand.TeleportToGame:
                return message.TargetGameServerId == _managedRuntimeState.ServerId && HandleTeleportToGame(message);
            case AvatarMigrationCommand.TeleportResult:
                return message.SourceGameServerId == _managedRuntimeState.ServerId && HandleTeleportResult(message);
            case AvatarMigrationCommand.MigrationOver:
                return HandleMigrationOver(message);
            default:
                return false;
            }
        }

        // KBE Cellapp::reqTeleportToCellApp: restore and enter in one dispatcher call.
        private bool HandleTeleportToGame(AvatarMigrationMessage request)
        {
            if (_lastTargetTeleportResults.TryGetValue(request.AvatarId, out var previous)
                && previous.MigrationId == request.MigrationId)
            {
                return IsSameMigration(previous, request)
                    && SendMigrationMessage(request.SourceGameServerId, previous);
            }
            if (_targetAvatarMigrations.TryGetValue(request.MigrationId, out var existing))
            {
                return IsSameMigration(existing.Message, request) && FinishTargetTeleport(existing);
            }
            if (!Entities.TryGetValue(request.TargetSpaceId, out var targetEntity)
                || targetEntity is not ServerSpaceEntity targetSpace || Entities.ContainsKey(request.AvatarId))
            {
                return SendTeleportFailure(request, "Target Space is unavailable or the Avatar already exists");
            }
            if (targetSpace.MaxPlayers > 0 && targetSpace.AvatarManageCmpt.Avatars.Count >= targetSpace.MaxPlayers)
            {
                return SendTeleportFailure(request, "Target Space is full");
            }

            ServerAvatarEntity avatar = null;
            TargetAvatarMigrationContext restored;
            bool endSent;
            try
            {
                avatar = CreateAvatarInstance();
                if (avatar == null)
                {
                    return SendTeleportFailure(request, "Failed to construct target Avatar");
                }
                avatar.InitializeEntity(request.AvatarData, EntitySerializationProfile.Migration, request.AvatarId);
                avatar.AttachToGateServer(request.GateServerId);
                avatar.ImmigateCmpt.BeginTeleport(
                    request.MigrationId, new EntityMailBox(request.TargetSpaceId, request.TargetGameServerId));
                RegisterLocalEntity(avatar);
                _avatarForwardingRoutes.Remove(avatar.Guid);
                if (request.ClientSessionId != 0)
                {
                    AvatarClientSessionIds[avatar.Guid] = request.ClientSessionId;
                }

                if (!targetSpace.AvatarManageCmpt.CanEnter(avatar))
                {
                    UnregisterLocalEntity(avatar);
                    return SendTeleportFailure(request, "Target Space admission was rejected");
                }
                var metadata = request.CreateResponse(AvatarMigrationCommand.TeleportToGame);
                restored = new TargetAvatarMigrationContext { Avatar = avatar, Message = metadata };
                _targetAvatarMigrations.Add(request.MigrationId, restored);
                // KBE reqTeleportToCellApp publishes End before its void addEntityAndEnterWorld call.
                endSent = SendMigrationMessage(request.GateServerId,
                    request.CreateResponse(AvatarMigrationCommand.MigrationEnd));
            }
            catch (Exception exception)
            {
                if (avatar != null)
                {
                    if (avatar.SpaceCmpt?.CurrentSpace != null)
                    {
                        avatar.SpaceCmpt.CurrentSpace.AvatarManageCmpt.Leave(avatar);
                    }
                    if (Entities.TryGetValue(request.AvatarId, out var registered) && ReferenceEquals(registered, avatar))
                    {
                        UnregisterLocalEntity(avatar);
                    }
                    else
                    {
                        avatar.UnInitializeEntity();
                    }
                }
                DELogger.Error(nameof(GameServerRuntimeState), $"Target Avatar restoration failed: {exception}");
                return SendTeleportFailure(request, exception.Message);
            }
            // After End, unexpected entry exceptions are not converted into a rollback or false success.
            targetSpace.AvatarManageCmpt.Enter(avatar);
            restored.EntryCompleted = true;
            return endSent && SendTargetTeleportResult(restored);
        }

        private bool FinishTargetTeleport(TargetAvatarMigrationContext context)
        {
            var message = context.Message;
            // A failed End send leaves a usable target plus the source Ghost; no false success is reported.
            if (!SendMigrationMessage(message.GateServerId, message.CreateResponse(AvatarMigrationCommand.MigrationEnd)))
            {
                return false;
            }
            return SendTargetTeleportResult(context);
        }

        private bool SendTargetTeleportResult(TargetAvatarMigrationContext context)
        {
            var message = context.Message;
            if (!context.EntryCompleted || context.Avatar.SpaceCmpt.CurrentSpace?.Guid != message.TargetSpaceId)
            {
                return false;
            }
            var result = message.CreateResponse(AvatarMigrationCommand.TeleportResult);
            _lastTargetTeleportResults[message.AvatarId] = result;
            return SendMigrationMessage(message.SourceGameServerId, result);
        }

        private bool SendTeleportFailure(AvatarMigrationMessage request, string error)
        {
            var response = request.CreateResponse(AvatarMigrationCommand.TeleportResult, false, error);
            _lastTargetTeleportResults[request.AvatarId] = response;
            return SendMigrationMessage(request.SourceGameServerId, response);
        }

        // KBE reqTeleportToCellAppCB destroys the source on success, or changes it back to Real on failure.
        private bool HandleTeleportResult(AvatarMigrationMessage response)
        {
            if (!_sourceAvatarMigrations.TryGetValue(response.MigrationId, out var context))
            {
                return true;
            }
            if (!IsSameMigration(context.Message, response)
                || context.Avatar.ImmigateCmpt.ImmigrationState != AvatarImmigrationState.Ghost)
            {
                return false;
            }
            if (!response.Success)
            {
                RestoreSourceAvatar(context, response.Error);
                return true;
            }

            // Like KBE destroyEntity(id, false), a Ghost must not send business RPC during cleanup.
            _sourceAvatarMigrations.Remove(response.MigrationId);
            if (_avatarForwardingRoutes.TryGetValue(response.AvatarId, out var route))
            {
                route.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(5);
            }
            UnregisterLocalEntity(context.Avatar);
            // Gate stopped new client input at Start. On the same Gate -> source session, all earlier
            // input precedes TeleportResult; its Ghost forwards also precede this reply back to Gate.
            return SendMigrationMessage(response.GateServerId,
                response.CreateResponse(AvatarMigrationCommand.SourceInputDrained));
        }

        // KBE reqTeleportToCellAppOver clears TELEPORT_START; the target is already in its Space.
        private bool HandleMigrationOver(AvatarMigrationMessage request)
        {
            var host = request.Success ? request.TargetGameServerId : request.SourceGameServerId;
            if (host != _managedRuntimeState.ServerId)
            {
                return false;
            }
            if (!request.Success)
            {
                return true;
            }
            if (!_targetAvatarMigrations.TryGetValue(request.MigrationId, out var context))
            {
                return _lastTargetTeleportResults.TryGetValue(request.AvatarId, out var result)
                    && result.Success && IsSameMigration(result, request);
            }
            if (!IsSameMigration(context.Message, request)
                || context.Avatar.ImmigateCmpt.ImmigrationState == AvatarImmigrationState.Ghost)
            {
                return false;
            }
            context.Avatar.ImmigateCmpt.ChangeToReal();
            // End may be followed by an entry exception. Retain that receipt so a duplicate transfer
            // cannot report failure and restore the source after the target has already published End.
            if (_lastTargetTeleportResults.TryGetValue(request.AvatarId, out var completed)
                && completed.Success && IsSameMigration(completed, request))
            {
                _targetAvatarMigrations.Remove(request.MigrationId);
            }
            return true;
        }

        private void RestoreSourceAvatar(SourceAvatarMigrationContext context, string error)
        {
            var avatar = context.Avatar;
            avatar.UnfreezePropertyWrites();
            if (context.AvatarData.Length != 0)
            {
                EntitySerializer.ApplySnapshot(avatar, EntitySerializationProfile.Migration, context.AvatarData);
            }
            avatar.ImmigateCmpt.ChangeToReal();
            _avatarForwardingRoutes.Remove(avatar.Guid);
            // Keep this context until restoration callbacks finish, to reject reentrant Teleport calls.

            // KBE's failure CB sends End to Base before restoring normal source callbacks.
            SendMigrationMessage(context.Message.GateServerId,
                context.Message.CreateResponse(AvatarMigrationCommand.MigrationEnd, false, error));
            if (context.SourceSpaceId != Guid.Empty && avatar.SpaceCmpt.CurrentSpace == null)
            {
                if (Entities.TryGetValue(context.SourceSpaceId, out var sourceEntity)
                    && sourceEntity is ServerSpaceEntity sourceSpace && sourceSpace.AvatarManageCmpt.CanEnter(avatar))
                {
                    sourceSpace.AvatarManageCmpt.Enter(avatar);
                }
                else
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Cannot restore source Space, avatarId={avatar.Guid}.");
                }
            }
            avatar.SpaceCmpt.NotifySpaceEnterFailed(error ?? "Avatar transfer failed");
            _sourceAvatarMigrations.Remove(context.Message.MigrationId);
        }

        private bool SendMigrationMessage(string destinationGameOrGate, AvatarMigrationMessage message)
        {
            var payload = AvatarMigrationProtocol.BuildSystemCommandPayload(destinationGameOrGate, message);
            var sent = NativeAPI.SendSystemCommandToServer(message.GateServerId, payload);
            if (!sent)
            {
                DELogger.Error(nameof(GameServerRuntimeState),
                    $"Failed Avatar migration send, avatarId={message.AvatarId}, command={message.Command}, destination={destinationGameOrGate}.");
            }
            return sent;
        }

        private ServerAvatarEntity CreateAvatarInstance()
        {
            return Activator.CreateInstance(AvatarType) as ServerAvatarEntity;
        }

        internal void ClearAvatarImmigrations()
        {
            _sourceAvatarMigrations.Clear();
            _targetAvatarMigrations.Clear();
            _lastTargetTeleportResults.Clear();
            _avatarForwardingRoutes.Clear();
        }

        internal static bool IsSameMigration(AvatarMigrationMessage left, AvatarMigrationMessage right)
        {
            return left != null && right != null && left.Version == right.Version
                && left.MigrationId == right.MigrationId && left.AvatarId == right.AvatarId
                && left.TargetSpaceId == right.TargetSpaceId && left.ClientSessionId == right.ClientSessionId
                && left.SourceGameServerId == right.SourceGameServerId && left.TargetGameServerId == right.TargetGameServerId
                && left.GateServerId == right.GateServerId;
        }

        internal bool UnregisterLocalEntity(ServerEntity entity)
        {
            if (entity == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), "Failed to unregister local Entity because Entity is null.");
                return false;
            }

            var removed = false;
            if (!Entities.TryGetValue(entity.Guid, out var registeredEntity))
            {
                DELogger.Error(nameof(GameServerRuntimeState),
                    $"Cannot unregister missing Entity, entityId={entity.Guid}, entityType={entity.GetType().FullName}.");
            }
            else if (!ReferenceEquals(registeredEntity, entity))
            {
                DELogger.Error(nameof(GameServerRuntimeState),
                    $"Cannot unregister a different Entity instance, entityId={entity.Guid}, entityType={entity.GetType().FullName}, "
                    + $"registeredType={registeredEntity?.GetType().FullName ?? "<null>"}.");
            }
            else
            {
                Entities.Remove(entity.Guid);
                _remoteEntityCreations.Remove(entity.Guid);
                removed = true;
            }

            if (entity is ServerAvatarEntity avatar)
            {
                if (!Avatars.TryGetValue(entity.Guid, out var registeredAvatar))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Cannot unregister missing Avatar, avatarId={entity.Guid}.");
                }
                else if (!ReferenceEquals(registeredAvatar, avatar))
                {
                    DELogger.Error(nameof(GameServerRuntimeState),
                        $"Cannot unregister a different Avatar instance, avatarId={entity.Guid}.");
                }
                else
                {
                    Avatars.Remove(entity.Guid);
                    AvatarClientSessionIds.Remove(entity.Guid);
                    removed = true;
                }
            }

            if (removed)
            {
                entity.UnInitializeEntity();
            }
            return removed;
        }

    }
}
