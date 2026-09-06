using System;
using System.Collections.Generic;
using DE.Server.Entities;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace DE.Server.NativeBridge
{
    public sealed partial class GameServerRuntimeState
    {
        private readonly Dictionary<Guid, SourceAvatarMigrationContext> _sourceAvatarMigrations = new();
        private readonly Dictionary<Guid, TargetAvatarMigrationContext> _targetAvatarMigrations = new();
        private readonly Dictionary<Guid, Guid> _lastAbortedTargetAvatarMigrations = new();
        private readonly Dictionary<Guid, Guid> _lastCompletedTargetAvatarMigrations = new();

        private bool BeginAvatarImmigration(AvatarEntity avatar, EntityMailBox targetSpace)
        {
            if (avatar == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), "Avatar immigration rejected because Avatar is null.");
                return false;
            }

            if (avatar.ImmigrationState != AvatarImmigrationState.Idle)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar immigration rejected because Avatar is not Idle, avatarId={avatar.Guid}, state={avatar.ImmigrationState}.");
                return false;
            }

            if (!avatar.IsAllowImmigrate())
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar immigration rejected because Avatar does not allow immigration, avatarId={avatar.Guid}.");
                return false;
            }

            if (!avatar.Proxy.IsValid)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar immigration rejected because Avatar Proxy is invalid, avatarId={avatar.Guid}.");
                return false;
            }

            if (!targetSpace.IsValid)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar immigration rejected because target Space MailBox is invalid, avatarId={avatar.Guid}, targetSpaceId={targetSpace.EntityId}, targetGameServerId={targetSpace.BindingGame}.");
                return false;
            }

            if (!Avatars.TryGetValue(avatar.Guid, out var registeredAvatar))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar immigration rejected because Avatar is not registered on the current Game, avatarId={avatar.Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredAvatar, avatar))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar immigration rejected because another Avatar instance is registered with the same id, avatarId={avatar.Guid}.");
                return false;
            }

            if (string.Equals(targetSpace.BindingGame, _managedRuntimeState.ServerId, StringComparison.Ordinal))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar immigration rejected because target Space is on the current Game and must use Teleport local flow, avatarId={avatar.Guid}, targetSpaceId={targetSpace.EntityId}, gameServerId={targetSpace.BindingGame}.");
                return false;
            }

            var migrationId = Guid.NewGuid();
            var message = new AvatarMigrationMessage
            {
                Command = AvatarMigrationCommand.FreezeRoute,
                MigrationId = migrationId,
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
                TargetSpace = targetSpace,
                SourceSpaceId = avatar.CurrentSpaceId,
            };
            _sourceAvatarMigrations.Add(migrationId, context);
            avatar.SetImmigrationState(
                AvatarImmigrationState.FreezingRoute,
                migrationId,
                targetSpace,
                message.SourceGameServerId,
                message.TargetGameServerId
            );
            if (!SendGateMigrationMessage(context, AvatarMigrationCommand.FreezeRoute))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to send Avatar route-freeze request, avatarId={avatar.Guid}, migrationId={migrationId}, gateServerId={message.GateServerId}.");
                FinishSourceMigration(context, AvatarImmigrationState.Failed);
                return false;
            }

            return true;
        }

        internal bool HandleAvatarMigrationGameMessage(string sourceServerId, ServerRpcPayload rpc)
        {
            if (!AvatarMigrationProtocol.TryDeserialize(rpc, out var message))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Received malformed Avatar migration Game message, sourceServerId={sourceServerId ?? "<null>"}, targetServerId={rpc.TargetServerId}, avatarId={rpc.EntityId}, methodId={rpc.MethodId}.");
                return false;
            }

            if (!string.Equals(sourceServerId, message.GateServerId, StringComparison.Ordinal))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Rejected Avatar migration Game message because source server is not the expected Gate, avatarId={message.AvatarId}, migrationId={message.MigrationId}, sourceServerId={sourceServerId ?? "<null>"}, expectedGateServerId={message.GateServerId}.");
                return false;
            }

            if (!string.Equals(rpc.TargetServerId, _managedRuntimeState.ServerId, StringComparison.Ordinal))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Rejected Avatar migration Game message because target Game does not match the current Game, avatarId={message.AvatarId}, migrationId={message.MigrationId}, targetGameServerId={rpc.TargetServerId}, currentGameServerId={_managedRuntimeState.ServerId}.");
                return false;
            }

            switch (message.Command)
            {
            case AvatarMigrationCommand.PrepareTarget:
                return HandlePrepareTarget(message);
            case AvatarMigrationCommand.ActivateTarget:
                return HandleActivateTarget(message);
            case AvatarMigrationCommand.CompleteTarget:
                return HandleCompleteTarget(message);
            case AvatarMigrationCommand.AbortTarget:
                return HandleAbortTarget(message);
            case AvatarMigrationCommand.RouteFrozen:
            case AvatarMigrationCommand.TargetPrepared:
            case AvatarMigrationCommand.TargetActivated:
            case AvatarMigrationCommand.RouteCommitted:
            case AvatarMigrationCommand.TargetCompleted:
            case AvatarMigrationCommand.TargetAborted:
            case AvatarMigrationCommand.RouteRolledBack:
                return HandleSourceMigrationResponse(message);
            default:
                DELogger.Error(nameof(GameServerRuntimeState), $"Rejected unsupported Avatar migration Game command, avatarId={message.AvatarId}, migrationId={message.MigrationId}, command={message.Command}.");
                return false;
            }
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
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to unregister local Entity because it is not in the Entity registry, entityId={entity.Guid}, entityType={entity.GetType().FullName}.");
            }
            else if (!ReferenceEquals(registeredEntity, entity))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to unregister local Entity because another instance is registered with the same id, entityId={entity.Guid}, entityType={entity.GetType().FullName}, registeredType={registeredEntity?.GetType().FullName ?? "<null>"}.");
            }
            else
            {
                Entities.Remove(entity.Guid);
                removed = true;
            }

            if (entity is AvatarEntity avatar)
            {
                if (!Avatars.TryGetValue(entity.Guid, out var registeredAvatar))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to unregister local Avatar because it is not in the Avatar registry, avatarId={entity.Guid}.");
                }
                else if (!ReferenceEquals(registeredAvatar, avatar))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to unregister local Avatar because another instance is registered with the same id, avatarId={entity.Guid}.");
                }
                else
                {
                    Avatars.Remove(entity.Guid);
                    AvatarClientSessionIds.Remove(entity.Guid);
                    removed = true;
                }
            }

            return removed;
        }

        internal void ClearAvatarImmigrations()
        {
            _sourceAvatarMigrations.Clear();
            _targetAvatarMigrations.Clear();
            _lastAbortedTargetAvatarMigrations.Clear();
            _lastCompletedTargetAvatarMigrations.Clear();
        }

        private bool HandleSourceMigrationResponse(AvatarMigrationMessage response)
        {
            if (response == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), "Rejected null Avatar source migration response.");
                return false;
            }

            if (!_sourceAvatarMigrations.TryGetValue(response.MigrationId, out var context))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Rejected Avatar source migration response because migration context does not exist, avatarId={response.AvatarId}, migrationId={response.MigrationId}, command={response.Command}.");
                return false;
            }

            if (!IsSameMigration(context.Message, response))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Rejected Avatar source migration response because it does not match the active migration, avatarId={response.AvatarId}, migrationId={response.MigrationId}, command={response.Command}.");
                return false;
            }

            var avatar = context.Avatar;
            if (avatar == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Rejected Avatar source migration response because context Avatar is null, avatarId={response.AvatarId}, migrationId={response.MigrationId}, command={response.Command}.");
                return false;
            }

            if (avatar.ImmigrationId != response.MigrationId)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Rejected Avatar source migration response because Avatar migration id does not match, avatarId={avatar.Guid}, avatarMigrationId={avatar.ImmigrationId}, responseMigrationId={response.MigrationId}, command={response.Command}.");
                return false;
            }

            switch (response.Command)
            {
            case AvatarMigrationCommand.RouteFrozen:
                if (avatar.ImmigrationState != AvatarImmigrationState.FreezingRoute)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Rejected route-frozen response because Avatar immigration state is unexpected, avatarId={avatar.Guid}, migrationId={response.MigrationId}, expectedState={AvatarImmigrationState.FreezingRoute}, actualState={avatar.ImmigrationState}.");
                    return true;
                }

                if (!response.Success)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Gate failed to freeze Avatar route, avatarId={response.AvatarId}, migrationId={response.MigrationId}, error={response.Error}.");
                    FinishSourceMigration(context, AvatarImmigrationState.Failed);
                    return true;
                }

                avatar.SetImmigrationState(
                    AvatarImmigrationState.Serializing,
                    response.MigrationId,
                    context.TargetSpace,
                    response.SourceGameServerId,
                    response.TargetGameServerId
                );
                if (context.SourceSpaceId != Guid.Empty)
                {
                    if (!Entities.TryGetValue(context.SourceSpaceId, out var frozenSourceEntity))
                    {
                        DELogger.Error(nameof(GameServerRuntimeState), $"Failed to leave source Space before Avatar serialization because the Space does not exist, avatarId={avatar.Guid}, migrationId={response.MigrationId}, sourceSpaceId={context.SourceSpaceId}.");
                    }
                    else if (frozenSourceEntity is not SpaceEntity frozenSourceSpace)
                    {
                        DELogger.Error(nameof(GameServerRuntimeState), $"Failed to leave source Space before Avatar serialization because the Entity is not a Space, avatarId={avatar.Guid}, migrationId={response.MigrationId}, sourceSpaceId={context.SourceSpaceId}, sourceEntityType={frozenSourceEntity?.GetType().FullName ?? "<null>"}.");
                    }
                    else if (!frozenSourceSpace.Leave(avatar))
                    {
                        DELogger.Error(nameof(GameServerRuntimeState), $"Failed to leave source Space before Avatar serialization, avatarId={avatar.Guid}, migrationId={response.MigrationId}, sourceSpaceId={context.SourceSpaceId}.");
                    }
                }

                try
                {
                    context.AvatarData = EntitySerializer.Serialize(avatar, EntitySerializeReason.Migrate);
                }
                catch (Exception exception)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to serialize Avatar for immigration: {exception}");
                    BeginAvatarMigrationRollback(context);
                    return true;
                }

                avatar.SetImmigrationState(
                    AvatarImmigrationState.PreparingTarget,
                    response.MigrationId,
                    context.TargetSpace,
                    response.SourceGameServerId,
                    response.TargetGameServerId
                );
                SendTargetMigrationMessage(context, AvatarMigrationCommand.PrepareTarget, true);
                return true;

            case AvatarMigrationCommand.TargetPrepared:
                if (avatar.ImmigrationState != AvatarImmigrationState.PreparingTarget)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Ignored target-prepared response because Avatar immigration state is unexpected, avatarId={avatar.Guid}, migrationId={response.MigrationId}, expectedState={AvatarImmigrationState.PreparingTarget}, actualState={avatar.ImmigrationState}.");
                    return true;
                }

                if (!response.Success)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Target rejected Avatar immigration preparation, avatarId={avatar.Guid}, migrationId={response.MigrationId}, error={response.Error}.");
                    BeginAvatarMigrationRollback(context);
                    return true;
                }

                context.TargetWasPrepared = true;
                context.AvatarData = Array.Empty<byte>();
                avatar.SetImmigrationState(
                    AvatarImmigrationState.TargetPrepared,
                    response.MigrationId,
                    context.TargetSpace,
                    response.SourceGameServerId,
                    response.TargetGameServerId
                );
                avatar.SetImmigrationState(
                    AvatarImmigrationState.ActivatingTarget,
                    response.MigrationId,
                    context.TargetSpace,
                    response.SourceGameServerId,
                    response.TargetGameServerId
                );
                SendTargetMigrationMessage(context, AvatarMigrationCommand.ActivateTarget, false);
                return true;

            case AvatarMigrationCommand.TargetActivated:
                if (avatar.ImmigrationState != AvatarImmigrationState.ActivatingTarget)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Ignored target-activated response because Avatar immigration state is unexpected, avatarId={avatar.Guid}, migrationId={response.MigrationId}, expectedState={AvatarImmigrationState.ActivatingTarget}, actualState={avatar.ImmigrationState}.");
                    return true;
                }

                if (!response.Success)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Target failed to activate immigrating Avatar, avatarId={avatar.Guid}, migrationId={response.MigrationId}, error={response.Error}.");
                    BeginAvatarMigrationRollback(context);
                    return true;
                }

                avatar.SetImmigrationState(
                    AvatarImmigrationState.TargetActivated,
                    response.MigrationId,
                    context.TargetSpace,
                    response.SourceGameServerId,
                    response.TargetGameServerId
                );
                avatar.SetImmigrationState(
                    AvatarImmigrationState.CommittingRoute,
                    response.MigrationId,
                    context.TargetSpace,
                    response.SourceGameServerId,
                    response.TargetGameServerId
                );
                SendGateMigrationMessage(context, AvatarMigrationCommand.CommitRoute);
                return true;

            case AvatarMigrationCommand.RouteCommitted:
                if (avatar.ImmigrationState == AvatarImmigrationState.CompletingTarget)
                {
                    return true;
                }

                if (avatar.ImmigrationState != AvatarImmigrationState.CommittingRoute)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Ignored route-committed response because Avatar immigration state is unexpected, avatarId={avatar.Guid}, migrationId={response.MigrationId}, expectedState={AvatarImmigrationState.CommittingRoute}, actualState={avatar.ImmigrationState}.");
                    return true;
                }

                if (!response.Success)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Gate failed to commit Avatar route, avatarId={avatar.Guid}, migrationId={response.MigrationId}, error={response.Error}.");
                    BeginAvatarMigrationRollback(context);
                    return true;
                }

                UnregisterLocalEntity(avatar);
                avatar.SetImmigrationState(
                    AvatarImmigrationState.CompletingTarget,
                    response.MigrationId,
                    context.TargetSpace,
                    response.SourceGameServerId,
                    response.TargetGameServerId
                );
                SendTargetMigrationMessage(context, AvatarMigrationCommand.CompleteTarget, false);
                return true;

            case AvatarMigrationCommand.TargetCompleted:
                if (avatar.ImmigrationState != AvatarImmigrationState.CompletingTarget)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Rejected target-completed response because Avatar immigration state is unexpected, avatarId={avatar.Guid}, migrationId={response.MigrationId}, expectedState={AvatarImmigrationState.CompletingTarget}, actualState={avatar.ImmigrationState}.");
                    return true;
                }

                if (!response.Success)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Target failed to complete Avatar immigration, avatarId={avatar.Guid}, migrationId={response.MigrationId}, error={response.Error}.");
                    return true;
                }

                FinishSourceMigration(context, AvatarImmigrationState.Completed);
                return true;

            case AvatarMigrationCommand.TargetAborted:
                if (avatar.ImmigrationState != AvatarImmigrationState.RollingBack)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Rejected target-aborted response because Avatar immigration state is unexpected, avatarId={avatar.Guid}, migrationId={response.MigrationId}, expectedState={AvatarImmigrationState.RollingBack}, actualState={avatar.ImmigrationState}.");
                    return true;
                }

                if (!response.Success)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Target failed to abort Avatar immigration, avatarId={avatar.Guid}, migrationId={response.MigrationId}, error={response.Error}.");
                    return true;
                }

                SendGateMigrationMessage(context, AvatarMigrationCommand.RollbackRoute);
                return true;

            case AvatarMigrationCommand.RouteRolledBack:
                if (avatar.ImmigrationState != AvatarImmigrationState.RollingBack)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Rejected route-rolled-back response because Avatar immigration state is unexpected, avatarId={avatar.Guid}, migrationId={response.MigrationId}, expectedState={AvatarImmigrationState.RollingBack}, actualState={avatar.ImmigrationState}.");
                    return true;
                }

                if (!response.Success)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Gate failed to roll back Avatar route, avatarId={avatar.Guid}, migrationId={response.MigrationId}, error={response.Error}.");
                }

                if (context.SourceSpaceId != Guid.Empty)
                {
                    if (!Entities.TryGetValue(context.SourceSpaceId, out var rollbackSourceEntity))
                    {
                        DELogger.Error(nameof(GameServerRuntimeState), $"Failed to restore Avatar after immigration rollback because source Space does not exist, avatarId={avatar.Guid}, migrationId={response.MigrationId}, sourceSpaceId={context.SourceSpaceId}.");
                    }
                    else if (rollbackSourceEntity is not SpaceEntity rollbackSourceSpace)
                    {
                        DELogger.Error(nameof(GameServerRuntimeState), $"Failed to restore Avatar after immigration rollback because source Entity is not a Space, avatarId={avatar.Guid}, migrationId={response.MigrationId}, sourceSpaceId={context.SourceSpaceId}, sourceEntityType={rollbackSourceEntity?.GetType().FullName ?? "<null>"}.");
                    }
                    else if (!rollbackSourceSpace.Enter(avatar))
                    {
                        DELogger.Error(nameof(GameServerRuntimeState), $"Failed to restore Avatar to source Space after immigration rollback, avatarId={avatar.Guid}, migrationId={response.MigrationId}, sourceSpaceId={context.SourceSpaceId}.");
                    }
                }

                FinishSourceMigration(context, AvatarImmigrationState.Failed);
                return true;

            default:
                DELogger.Error(nameof(GameServerRuntimeState), $"Rejected unsupported Avatar source migration response command, avatarId={response.AvatarId}, migrationId={response.MigrationId}, command={response.Command}.");
                return false;
            }
        }

        private bool HandlePrepareTarget(AvatarMigrationMessage request)
        {
            if (request == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), "Failed to prepare target Avatar because migration request is null.");
                return false;
            }

            if (!string.Equals(request.TargetGameServerId, _managedRuntimeState.ServerId, StringComparison.Ordinal))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because target Game does not match the current Game, avatarId={request.AvatarId}, migrationId={request.MigrationId}, targetGameServerId={request.TargetGameServerId}, currentGameServerId={_managedRuntimeState.ServerId}.");
                return false;
            }

            if (_lastAbortedTargetAvatarMigrations.TryGetValue(request.AvatarId, out var abortedMigrationId))
            {
                if (abortedMigrationId == request.MigrationId)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because migration was already aborted, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "migration was aborted");
                }
            }

            if (_lastCompletedTargetAvatarMigrations.TryGetValue(request.AvatarId, out var completedMigrationId))
            {
                if (completedMigrationId == request.MigrationId)
                {
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, true);
                }
            }

            if (_targetAvatarMigrations.TryGetValue(request.MigrationId, out var existingContext))
            {
                var isSameMigration = IsSameMigration(existingContext.Message, request);
                if (!isSameMigration)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because migration id collides with another migration, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "migration id collision");
                }

                if (existingContext.Avatar == null)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because existing migration context has no Avatar, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "prepared Avatar does not exist");
                }

                if (existingContext.IsActivated)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because existing migration is already activated, avatarId={request.AvatarId}, migrationId={request.MigrationId}, state={existingContext.Avatar.ImmigrationState}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "target Avatar is already activated");
                }

                if (existingContext.Avatar.ImmigrationState != AvatarImmigrationState.TargetPrepared)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because existing Avatar state is unexpected, avatarId={request.AvatarId}, migrationId={request.MigrationId}, expectedState={AvatarImmigrationState.TargetPrepared}, actualState={existingContext.Avatar.ImmigrationState}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "prepared Avatar state is invalid");
                }

                return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, true);
            }

            if (request.TargetSpaceId == Guid.Empty)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because target Space id is empty, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "target Space id is empty");
            }

            if (!Entities.TryGetValue(request.TargetSpaceId, out var targetEntity))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because target Space does not exist on target Game, avatarId={request.AvatarId}, migrationId={request.MigrationId}, targetSpaceId={request.TargetSpaceId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "target Space does not exist on target Game");
            }

            if (targetEntity is not SpaceEntity)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because target Entity is not a Space, avatarId={request.AvatarId}, migrationId={request.MigrationId}, targetSpaceId={request.TargetSpaceId}, targetEntityType={targetEntity?.GetType().FullName ?? "<null>"}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "target entity is not a Space");
            }

            if (Entities.ContainsKey(request.AvatarId))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because an Entity with the Avatar id already exists, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "an entity with the Avatar id already exists on target Game");
            }

            _lastAbortedTargetAvatarMigrations.Remove(request.AvatarId);
            _lastCompletedTargetAvatarMigrations.Remove(request.AvatarId);

            AvatarEntity candidate = null;
            try
            {
                candidate = CreateAvatarInstance();
                if (candidate == null)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because Avatar construction returned null, avatarId={request.AvatarId}, migrationId={request.MigrationId}, avatarType={AvatarType?.FullName ?? "<null>"}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "failed to construct target Avatar");
                }

                if (candidate.ImmigrationState != AvatarImmigrationState.Idle)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because newly constructed Avatar is not Idle, avatarId={request.AvatarId}, migrationId={request.MigrationId}, state={candidate.ImmigrationState}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "new target Avatar is not Idle");
                }

                if (!EntitySerializer.TryDeserialize(candidate, EntitySerializeReason.Migrate, request.AvatarData))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because migration snapshot could not be deserialized, avatarId={request.AvatarId}, migrationId={request.MigrationId}, snapshotSize={request.AvatarData?.Length ?? 0}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "failed to deserialize Avatar migration snapshot");
                }

                if (candidate.Guid != request.AvatarId)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare target Avatar because snapshot Avatar id does not match migration request, requestAvatarId={request.AvatarId}, snapshotAvatarId={candidate.Guid}, migrationId={request.MigrationId}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "Avatar migration snapshot id mismatch");
                }

                candidate.AttachToGateServer(request.GateServerId);
                request.AvatarData = Array.Empty<byte>();
                var targetSpace = new EntityMailBox(request.TargetSpaceId, request.TargetGameServerId);
                candidate.SetImmigrationState(
                    AvatarImmigrationState.TargetPrepared,
                    request.MigrationId,
                    targetSpace,
                    request.SourceGameServerId,
                    request.TargetGameServerId
                );
                _targetAvatarMigrations.Add(
                    request.MigrationId,
                    new TargetAvatarMigrationContext
                    {
                        Avatar = candidate,
                        Message = request,
                    }
                );
                return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, true);
            }
            catch (Exception exception)
            {
                if (candidate != null)
                {
                    UnregisterLocalEntity(candidate);
                }

                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to prepare immigrating Avatar: {exception}");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, exception.Message);
            }
        }

        private bool HandleActivateTarget(AvatarMigrationMessage request)
        {
            if (request == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), "Failed to activate target Avatar because migration request is null.");
                return false;
            }

            if (!_targetAvatarMigrations.TryGetValue(request.MigrationId, out var context))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to activate target Avatar because migration context does not exist, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "target Avatar was not prepared");
            }

            if (!IsSameMigration(context.Message, request))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to activate target Avatar because request does not match prepared migration, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "target Avatar migration does not match prepared migration");
            }

            var avatar = context.Avatar;
            if (avatar == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to activate target Avatar because migration context Avatar is null, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "prepared Avatar does not exist");
            }

            if (avatar.ImmigrationId != request.MigrationId)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to activate target Avatar because Avatar migration id does not match, avatarId={request.AvatarId}, requestMigrationId={request.MigrationId}, avatarMigrationId={avatar.ImmigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "prepared Avatar migration id mismatch");
            }

            if (context.IsActivated)
            {
                if (avatar.ImmigrationState != AvatarImmigrationState.TargetActivated)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to handle repeated target activation because Avatar state is unexpected, avatarId={request.AvatarId}, migrationId={request.MigrationId}, expectedState={AvatarImmigrationState.TargetActivated}, actualState={avatar.ImmigrationState}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "activated Avatar state is invalid");
                }

                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, true);
            }

            if (avatar.ImmigrationState != AvatarImmigrationState.TargetPrepared)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to activate target Avatar because Avatar state is unexpected, avatarId={request.AvatarId}, migrationId={request.MigrationId}, expectedState={AvatarImmigrationState.TargetPrepared}, actualState={avatar.ImmigrationState}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "prepared Avatar state is invalid");
            }

            if (Avatars.TryGetValue(avatar.Guid, out var existingAvatar))
            {
                if (!ReferenceEquals(existingAvatar, avatar))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to activate target Avatar because another Avatar instance is active, avatarId={avatar.Guid}, migrationId={request.MigrationId}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "another Avatar instance is active on target Game");
                }
            }

            try
            {
                RegisterLocalEntity(avatar);
                if (request.ClientSessionId != 0)
                {
                    AvatarClientSessionIds[avatar.Guid] = request.ClientSessionId;
                }

                if (!Entities.TryGetValue(request.TargetSpaceId, out var targetEntity))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to activate target Avatar because target Space does not exist, avatarId={avatar.Guid}, migrationId={request.MigrationId}, targetSpaceId={request.TargetSpaceId}.");
                    UnregisterLocalEntity(avatar);
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "target Space does not exist");
                }

                if (targetEntity is not SpaceEntity targetSpace)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to activate target Avatar because target Entity is not a Space, avatarId={avatar.Guid}, migrationId={request.MigrationId}, targetSpaceId={request.TargetSpaceId}, targetEntityType={targetEntity?.GetType().FullName ?? "<null>"}.");
                    UnregisterLocalEntity(avatar);
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "target entity is not a Space");
                }

                if (!targetSpace.Enter(avatar))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to activate target Avatar because Avatar could not enter target Space, avatarId={avatar.Guid}, migrationId={request.MigrationId}, targetSpaceId={request.TargetSpaceId}.");
                    UnregisterLocalEntity(avatar);
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "failed to enter target Space");
                }

                avatar.SetImmigrationState(
                    AvatarImmigrationState.TargetActivated,
                    request.MigrationId,
                    new EntityMailBox(request.TargetSpaceId, request.TargetGameServerId),
                    request.SourceGameServerId,
                    request.TargetGameServerId
                );
                context.IsActivated = true;
                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, true);
            }
            catch (Exception exception)
            {
                if (!Entities.TryGetValue(request.TargetSpaceId, out var targetEntity))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to clean up target Avatar after activation exception because target Space does not exist, avatarId={avatar.Guid}, migrationId={request.MigrationId}, targetSpaceId={request.TargetSpaceId}.");
                }
                else if (targetEntity is not SpaceEntity targetSpace)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to clean up target Avatar after activation exception because target Entity is not a Space, avatarId={avatar.Guid}, migrationId={request.MigrationId}, targetSpaceId={request.TargetSpaceId}, targetEntityType={targetEntity?.GetType().FullName ?? "<null>"}.");
                }
                else if (!targetSpace.Leave(avatar))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to remove target Avatar from target Space after activation exception, avatarId={avatar.Guid}, migrationId={request.MigrationId}, targetSpaceId={request.TargetSpaceId}.");
                }

                UnregisterLocalEntity(avatar);
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to activate target Avatar: {exception}");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, exception.Message);
            }
        }

        private bool HandleCompleteTarget(AvatarMigrationMessage request)
        {
            if (request == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), "Failed to complete target Avatar because migration request is null.");
                return false;
            }

            if (_lastCompletedTargetAvatarMigrations.TryGetValue(request.AvatarId, out var completedMigrationId))
            {
                if (completedMigrationId == request.MigrationId)
                {
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetCompleted, true);
                }
            }

            if (!_targetAvatarMigrations.TryGetValue(request.MigrationId, out var context))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to complete target Avatar because migration context does not exist, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetCompleted, false, "target Avatar is not active");
            }

            if (!IsSameMigration(context.Message, request))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to complete target Avatar because request does not match active migration, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetCompleted, false, "target Avatar migration does not match active migration");
            }

            if (!context.IsActivated)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to complete target Avatar because Avatar is not active, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetCompleted, false, "target Avatar is not active");
            }

            if (context.Avatar == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to complete target Avatar because migration context Avatar is null, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetCompleted, false, "active Avatar does not exist");
            }

            if (context.Avatar.ImmigrationId != request.MigrationId)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to complete target Avatar because Avatar migration id does not match, avatarId={request.AvatarId}, requestMigrationId={request.MigrationId}, avatarMigrationId={context.Avatar.ImmigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetCompleted, false, "active Avatar migration id mismatch");
            }

            if (context.Avatar.ImmigrationState != AvatarImmigrationState.TargetActivated)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to complete target Avatar because Avatar state is unexpected, avatarId={request.AvatarId}, migrationId={request.MigrationId}, expectedState={AvatarImmigrationState.TargetActivated}, actualState={context.Avatar.ImmigrationState}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetCompleted, false, "active Avatar state is invalid");
            }

            context.Avatar.SetImmigrationState(
                AvatarImmigrationState.Completed,
                request.MigrationId,
                new EntityMailBox(request.TargetSpaceId, request.TargetGameServerId),
                request.SourceGameServerId,
                request.TargetGameServerId
            );
            _lastCompletedTargetAvatarMigrations[request.AvatarId] = request.MigrationId;
            _lastAbortedTargetAvatarMigrations.Remove(request.AvatarId);
            _targetAvatarMigrations.Remove(request.MigrationId);

            return SendTargetResponse(request, AvatarMigrationCommand.TargetCompleted, true);
        }

        private bool HandleAbortTarget(AvatarMigrationMessage request)
        {
            if (request == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), "Failed to abort target Avatar because migration request is null.");
                return false;
            }

            if (_lastCompletedTargetAvatarMigrations.TryGetValue(request.AvatarId, out var completedMigrationId))
            {
                if (completedMigrationId == request.MigrationId)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to abort target Avatar because migration is already completed, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetAborted, false, "target migration is already completed");
                }
            }

            if (_lastAbortedTargetAvatarMigrations.TryGetValue(request.AvatarId, out var abortedMigrationId))
            {
                if (abortedMigrationId == request.MigrationId)
                {
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetAborted, true);
                }
            }

            if (!_targetAvatarMigrations.TryGetValue(request.MigrationId, out var context))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to abort target Avatar because migration context does not exist, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetAborted, false, "target migration context does not exist");
            }

            if (!IsSameMigration(context.Message, request))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to abort target Avatar because request does not match active migration, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetAborted, false, "target migration request does not match active migration");
            }

            if (context.Avatar == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to abort target Avatar because migration context Avatar is null, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetAborted, false, "target Avatar does not exist");
            }

            if (context.Avatar.ImmigrationId != request.MigrationId)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to abort target Avatar because Avatar migration id does not match, avatarId={request.AvatarId}, requestMigrationId={request.MigrationId}, avatarMigrationId={context.Avatar.ImmigrationId}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetAborted, false, "target Avatar migration id mismatch");
            }

            if (context.IsActivated)
            {
                if (context.Avatar.ImmigrationState != AvatarImmigrationState.TargetActivated)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to abort active target Avatar because Avatar state is unexpected, avatarId={request.AvatarId}, migrationId={request.MigrationId}, expectedState={AvatarImmigrationState.TargetActivated}, actualState={context.Avatar.ImmigrationState}.");
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetAborted, false, "active target Avatar state is invalid");
                }

                if (!Entities.TryGetValue(request.TargetSpaceId, out var targetEntity))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to remove aborted target Avatar from Space because target Space does not exist, avatarId={request.AvatarId}, migrationId={request.MigrationId}, targetSpaceId={request.TargetSpaceId}.");
                }
                else if (targetEntity is not SpaceEntity targetSpace)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to remove aborted target Avatar from Space because target Entity is not a Space, avatarId={request.AvatarId}, migrationId={request.MigrationId}, targetSpaceId={request.TargetSpaceId}, targetEntityType={targetEntity?.GetType().FullName ?? "<null>"}.");
                }
                else if (!targetSpace.Leave(context.Avatar))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to remove aborted target Avatar from target Space, avatarId={request.AvatarId}, migrationId={request.MigrationId}, targetSpaceId={request.TargetSpaceId}.");
                }

                if (!UnregisterLocalEntity(context.Avatar))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Failed to unregister aborted target Avatar, avatarId={request.AvatarId}, migrationId={request.MigrationId}.");
                }
            }
            else if (context.Avatar.ImmigrationState != AvatarImmigrationState.TargetPrepared)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to abort prepared target Avatar because Avatar state is unexpected, avatarId={request.AvatarId}, migrationId={request.MigrationId}, expectedState={AvatarImmigrationState.TargetPrepared}, actualState={context.Avatar.ImmigrationState}.");
                return SendTargetResponse(request, AvatarMigrationCommand.TargetAborted, false, "prepared target Avatar state is invalid");
            }

            context.Avatar.SetImmigrationState(
                AvatarImmigrationState.Failed,
                request.MigrationId,
                new EntityMailBox(request.TargetSpaceId, request.TargetGameServerId),
                request.SourceGameServerId,
                request.TargetGameServerId
            );
            _lastAbortedTargetAvatarMigrations[request.AvatarId] = request.MigrationId;
            _targetAvatarMigrations.Remove(request.MigrationId);
            return SendTargetResponse(request, AvatarMigrationCommand.TargetAborted, true);
        }

        private void BeginAvatarMigrationRollback(SourceAvatarMigrationContext context)
        {
            context.Avatar.SetImmigrationState(
                AvatarImmigrationState.RollingBack,
                context.Message.MigrationId,
                context.TargetSpace,
                context.Message.SourceGameServerId,
                context.Message.TargetGameServerId
            );
            if (context.TargetWasPrepared)
            {
                SendTargetMigrationMessage(context, AvatarMigrationCommand.AbortTarget, false);
                return;
            }

            SendGateMigrationMessage(context, AvatarMigrationCommand.RollbackRoute);
        }

        private AvatarEntity CreateAvatarInstance()
        {
            return Activator.CreateInstance(AvatarType) as AvatarEntity;
        }

        private bool SendGateMigrationMessage(SourceAvatarMigrationContext context, AvatarMigrationCommand command)
        {
            var message = CopyMessage(context.Message, command);
            var payload = AvatarMigrationProtocol.BuildServerRpcPayload(
                ServerRpcTargetKind.AvatarMigrationGate,
                string.Empty,
                message
            );
            var sent = NativeAPI.SendServerRpcToServer(message.GateServerId, payload);
            if (!sent)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to send Avatar migration message to Gate, avatarId={message.AvatarId}, migrationId={message.MigrationId}, command={command}, gateServerId={message.GateServerId}.");
            }

            return sent;
        }

        private bool SendTargetMigrationMessage(
            SourceAvatarMigrationContext context,
            AvatarMigrationCommand command,
            bool includeAvatarData)
        {
            var message = CopyMessage(context.Message, command);
            message.AvatarData = includeAvatarData ? context.AvatarData : Array.Empty<byte>();
            var payload = AvatarMigrationProtocol.BuildServerRpcPayload(
                ServerRpcTargetKind.AvatarMigrationGame,
                message.TargetGameServerId,
                message
            );
            var sent = NativeAPI.SendServerRpcToServer(message.GateServerId, payload);
            if (!sent)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to send Avatar migration message to target Game through Gate, avatarId={message.AvatarId}, migrationId={message.MigrationId}, command={command}, gateServerId={message.GateServerId}, targetGameServerId={message.TargetGameServerId}.");
            }

            return sent;
        }

        private bool SendTargetResponse(
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
            var sent = NativeAPI.SendServerRpcToServer(request.GateServerId, payload);
            if (!sent)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to send Avatar migration response through Gate, avatarId={request.AvatarId}, migrationId={request.MigrationId}, command={command}, success={success}, gateServerId={request.GateServerId}, sourceGameServerId={request.SourceGameServerId}.");
            }

            return sent;
        }

        private void FinishSourceMigration(SourceAvatarMigrationContext context, AvatarImmigrationState state)
        {
            context.Avatar.SetImmigrationState(
                state,
                context.Message.MigrationId,
                context.TargetSpace,
                context.Message.SourceGameServerId,
                context.Message.TargetGameServerId
            );
            _sourceAvatarMigrations.Remove(context.Message.MigrationId);
        }

        private static AvatarMigrationMessage CopyMessage(AvatarMigrationMessage source, AvatarMigrationCommand command)
        {
            return new AvatarMigrationMessage
            {
                Command = command,
                MigrationId = source.MigrationId,
                AvatarId = source.AvatarId,
                TargetSpaceId = source.TargetSpaceId,
                SourceGameServerId = source.SourceGameServerId,
                TargetGameServerId = source.TargetGameServerId,
                GateServerId = source.GateServerId,
                ClientSessionId = source.ClientSessionId,
            };
        }

        internal static bool IsSameMigration(AvatarMigrationMessage left, AvatarMigrationMessage right)
        {
            if (left == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), "Avatar migration comparison failed because existing message is null.");
                return false;
            }

            if (right == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar migration comparison failed because incoming message is null, existingAvatarId={left.AvatarId}, existingMigrationId={left.MigrationId}.");
                return false;
            }

            if (left.MigrationId != right.MigrationId)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar migration comparison failed because migration ids differ, existingMigrationId={left.MigrationId}, incomingMigrationId={right.MigrationId}, avatarId={right.AvatarId}.");
                return false;
            }

            if (left.AvatarId != right.AvatarId)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar migration comparison failed because Avatar ids differ, migrationId={right.MigrationId}, existingAvatarId={left.AvatarId}, incomingAvatarId={right.AvatarId}.");
                return false;
            }

            if (left.TargetSpaceId != right.TargetSpaceId)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar migration comparison failed because target Space ids differ, avatarId={right.AvatarId}, migrationId={right.MigrationId}, existingTargetSpaceId={left.TargetSpaceId}, incomingTargetSpaceId={right.TargetSpaceId}.");
                return false;
            }

            if (left.ClientSessionId != right.ClientSessionId)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar migration comparison failed because client session ids differ, avatarId={right.AvatarId}, migrationId={right.MigrationId}, existingClientSessionId={left.ClientSessionId}, incomingClientSessionId={right.ClientSessionId}.");
                return false;
            }

            if (!string.Equals(left.SourceGameServerId, right.SourceGameServerId, StringComparison.Ordinal))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar migration comparison failed because source Game ids differ, avatarId={right.AvatarId}, migrationId={right.MigrationId}, existingSourceGameServerId={left.SourceGameServerId}, incomingSourceGameServerId={right.SourceGameServerId}.");
                return false;
            }

            if (!string.Equals(left.TargetGameServerId, right.TargetGameServerId, StringComparison.Ordinal))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar migration comparison failed because target Game ids differ, avatarId={right.AvatarId}, migrationId={right.MigrationId}, existingTargetGameServerId={left.TargetGameServerId}, incomingTargetGameServerId={right.TargetGameServerId}.");
                return false;
            }

            if (!string.Equals(left.GateServerId, right.GateServerId, StringComparison.Ordinal))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar migration comparison failed because Gate ids differ, avatarId={right.AvatarId}, migrationId={right.MigrationId}, existingGateServerId={left.GateServerId}, incomingGateServerId={right.GateServerId}.");
                return false;
            }

            return true;
        }
    }
}
