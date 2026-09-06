using System;
using System.Collections.Generic;
using System.Text.Json;
using DE.Server.Entities;
using DE.Server.NativeBridge;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public enum AvatarImmigrationState
    {
        Idle = 0,
        FreezingRoute = 1,
        Serializing = 2,
        PreparingTarget = 3,
        TargetPrepared = 4,
        ActivatingTarget = 5,
        TargetActivated = 6,
        CommittingRoute = 7,
        CompletingTarget = 8,
        Completed = 9,
        RollingBack = 10,
        Failed = 11,
    }

    public partial class AvatarEntity
    {
        public AvatarImmigrationState ImmigrationState { get; private set; } = AvatarImmigrationState.Idle;
        public Guid ImmigrationId { get; private set; }
        public EntityMailBox ImmigrationTargetSpace { get; private set; }

        public override bool IsAllowImmigrate()
        {
            return true;
        }

        public bool Immigrate(EntityMailBox targetSpace)
        {
            return ManagedRuntimeState
                .RequireCurrentGameServerRuntimeState()
                .BeginAvatarImmigration(this, targetSpace);
        }

        internal void SetImmigrationState(
            AvatarImmigrationState state,
            Guid migrationId,
            EntityMailBox targetSpace,
            string sourceGameServerId,
            string targetGameServerId)
        {
            ImmigrationState = state;
            ImmigrationId = migrationId;
            ImmigrationTargetSpace = targetSpace;
            DELogger.Info(
                nameof(AvatarEntity),
                $"Avatar immigration state changed, avatarId={Guid}, migrationId={migrationId}, state={state}, sourceGameServerId={sourceGameServerId}, targetGameServerId={targetGameServerId}, targetSpaceId={targetSpace.EntityId}."
            );
        }

    }
}

namespace DE.Server.NativeBridge
{
    internal enum AvatarMigrationCommand : ushort
    {
        FreezeRoute = 1,
        RouteFrozen = 2,
        PrepareTarget = 3,
        TargetPrepared = 4,
        ActivateTarget = 5,
        TargetActivated = 6,
        CommitRoute = 7,
        RouteCommitted = 8,
        CompleteTarget = 9,
        TargetCompleted = 10,
        RollbackRoute = 11,
        RouteRolledBack = 12,
        AbortTarget = 13,
        TargetAborted = 14,
    }

    internal sealed class AvatarMigrationMessage
    {
        public const ushort CurrentVersion = 1;

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

        public AvatarMigrationMessage CreateResponse(AvatarMigrationCommand command, bool success, string error = null)
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

        public static byte[] BuildServerRpcPayload(
            ServerRpcTargetKind targetKind,
            string targetServerId,
            AvatarMigrationMessage message)
        {
            var messagePayload = JsonSerializer.SerializeToUtf8Bytes(message, s_jsonOptions);
            return new ServerRpcPayload
            {
                Version = ServerRpcPayload.CurrentVersion,
                TargetKind = targetKind,
                EntityId = message.AvatarId,
                TargetServerId = targetServerId ?? string.Empty,
                StubName = string.Empty,
                MethodId = (uint)message.Command,
                ArgsPayload = messagePayload,
            }.Serialize();
        }

        public static bool TryDeserialize(ServerRpcPayload rpc, out AvatarMigrationMessage message)
        {
            message = null;
            if (rpc.ArgsPayload == null
                || rpc.ArgsPayload.Length == 0
                || rpc.ArgsPayload.Length > MaxPayloadSizeBytes)
            {
                return false;
            }

            try
            {
                message = JsonSerializer.Deserialize<AvatarMigrationMessage>(rpc.ArgsPayload, s_jsonOptions);
            }
            catch (JsonException)
            {
                return false;
            }

            return message != null
                && message.Version == AvatarMigrationMessage.CurrentVersion
                && message.MigrationId != Guid.Empty
                && message.AvatarId != Guid.Empty
                && message.AvatarId == rpc.EntityId
                && (uint)message.Command == rpc.MethodId
                && !string.IsNullOrWhiteSpace(message.SourceGameServerId)
                && !string.IsNullOrWhiteSpace(message.TargetGameServerId)
                && !string.IsNullOrWhiteSpace(message.GateServerId);
        }
    }

    internal sealed class SourceAvatarMigrationContext
    {
        public AvatarEntity Avatar;
        public AvatarMigrationMessage Message;
        public EntityMailBox TargetSpace;
        public Guid SourceSpaceId;
        public byte[] AvatarData = Array.Empty<byte>();
        public ulong RetryTimerId;
        public bool TargetWasPrepared;
        public bool TargetWasAborted;
    }

    internal sealed class TargetAvatarMigrationContext
    {
        public AvatarEntity Avatar;
        public AvatarMigrationMessage Message;
        public bool IsActivated;
    }

    public sealed partial class GameServerRuntimeState
    {
        private const int AvatarMigrationRetryMilliseconds = 3000;

        private readonly Dictionary<Guid, SourceAvatarMigrationContext> _sourceAvatarMigrations =
            new Dictionary<Guid, SourceAvatarMigrationContext>();
        private readonly Dictionary<Guid, TargetAvatarMigrationContext> _targetAvatarMigrations =
            new Dictionary<Guid, TargetAvatarMigrationContext>();
        private readonly Dictionary<Guid, Guid> _lastAbortedTargetAvatarMigrations = new Dictionary<Guid, Guid>();
        private readonly Dictionary<Guid, Guid> _lastCompletedTargetAvatarMigrations = new Dictionary<Guid, Guid>();

        internal bool BeginAvatarImmigration(AvatarEntity avatar, EntityMailBox targetSpace)
        {
            if (avatar == null
                || !avatar.IsAllowImmigrate()
                || !avatar.MailBox.IsValid
                || !avatar.Proxy.IsValid
                || !targetSpace.IsValid
                || !Avatars.TryGetValue(avatar.Guid, out var registeredAvatar)
                || !ReferenceEquals(registeredAvatar, avatar))
            {
                return false;
            }

            if (avatar.ImmigrationState != AvatarImmigrationState.Idle
                && avatar.ImmigrationState != AvatarImmigrationState.Completed
                && avatar.ImmigrationState != AvatarImmigrationState.Failed)
            {
                DELogger.Warn(
                    nameof(GameServerRuntimeState),
                    $"Avatar immigration is already active, avatarId={avatar.Guid}, state={avatar.ImmigrationState}."
                );
                return false;
            }

            if (string.Equals(targetSpace.BindingGame, _managedRuntimeState.ServerId, StringComparison.Ordinal))
            {
                var localMigrationId = Guid.NewGuid();
                avatar.SetImmigrationState(
                    AvatarImmigrationState.Serializing,
                    localMigrationId,
                    targetSpace,
                    _managedRuntimeState.ServerId,
                    targetSpace.BindingGame
                );
                var entered = Entities.TryGetValue(targetSpace.EntityId, out var targetEntity)
                    && targetEntity is SpaceEntity localTargetSpace
                    && localTargetSpace.Enter(avatar);
                avatar.SetImmigrationState(
                    entered ? AvatarImmigrationState.Completed : AvatarImmigrationState.Failed,
                    localMigrationId,
                    targetSpace,
                    _managedRuntimeState.ServerId,
                    targetSpace.BindingGame
                );
                return entered;
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
            StartAvatarMigrationRetryTimer(context);
            if (!SendGateMigrationMessage(context, AvatarMigrationCommand.FreezeRoute)
                && context.RetryTimerId == 0)
            {
                FinishSourceMigration(context, AvatarImmigrationState.Failed);
                return false;
            }

            return true;
        }

        internal bool HandleAvatarMigrationGameMessage(string sourceServerId, ServerRpcPayload rpc)
        {
            if (!AvatarMigrationProtocol.TryDeserialize(rpc, out var message)
                || !string.Equals(sourceServerId, message.GateServerId, StringComparison.Ordinal)
                || !string.Equals(rpc.TargetServerId, _managedRuntimeState.ServerId, StringComparison.Ordinal))
            {
                DELogger.Warn(nameof(GameServerRuntimeState), "Received invalid Avatar migration Game message.");
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
                return false;
            }
        }

        internal bool UnregisterLocalEntity(ServerEntity entity)
        {
            if (entity == null)
            {
                return false;
            }

            var removed = false;
            if (Entities.TryGetValue(entity.Guid, out var registeredEntity)
                && ReferenceEquals(registeredEntity, entity))
            {
                Entities.Remove(entity.Guid);
                removed = true;
            }

            if (entity is AvatarEntity avatar
                && Avatars.TryGetValue(entity.Guid, out var registeredAvatar)
                && ReferenceEquals(registeredAvatar, avatar))
            {
                Avatars.Remove(entity.Guid);
                AvatarClientSessionIds.Remove(entity.Guid);
                removed = true;
            }

            return removed;
        }

        internal void ClearAvatarImmigrations()
        {
            foreach (var context in _sourceAvatarMigrations.Values)
            {
                CancelAvatarMigrationRetryTimer(context);
            }

            _sourceAvatarMigrations.Clear();
            _targetAvatarMigrations.Clear();
            _lastAbortedTargetAvatarMigrations.Clear();
            _lastCompletedTargetAvatarMigrations.Clear();
        }

        private bool HandleSourceMigrationResponse(AvatarMigrationMessage response)
        {
            if (!_sourceAvatarMigrations.TryGetValue(response.MigrationId, out var context)
                || !IsSameMigration(context.Message, response))
            {
                return false;
            }

            var avatar = context.Avatar;
            switch (response.Command)
            {
            case AvatarMigrationCommand.RouteFrozen:
                if (!response.Success)
                {
                    FinishSourceMigration(context, AvatarImmigrationState.Failed);
                    return true;
                }

                if (avatar.ImmigrationState != AvatarImmigrationState.FreezingRoute)
                {
                    return true;
                }

                avatar.SetImmigrationState(
                    AvatarImmigrationState.Serializing,
                    response.MigrationId,
                    context.TargetSpace,
                    response.SourceGameServerId,
                    response.TargetGameServerId
                );
                if (context.SourceSpaceId != Guid.Empty
                    && Entities.TryGetValue(context.SourceSpaceId, out var frozenSourceEntity)
                    && frozenSourceEntity is SpaceEntity frozenSourceSpace)
                {
                    frozenSourceSpace.Leave(avatar);
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
                    return true;
                }

                if (!response.Success)
                {
                    DELogger.Warn(nameof(GameServerRuntimeState), $"Target rejected Avatar immigration preparation: {response.Error}");
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
                    return true;
                }

                if (!response.Success)
                {
                    DELogger.Warn(nameof(GameServerRuntimeState), $"Target failed to activate immigrating Avatar: {response.Error}");
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
                if (!response.Success)
                {
                    BeginAvatarMigrationRollback(context);
                    return true;
                }

                if (avatar.ImmigrationState == AvatarImmigrationState.CompletingTarget)
                {
                    return true;
                }

                if (avatar.ImmigrationState != AvatarImmigrationState.CommittingRoute)
                {
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
                if (response.Success)
                {
                    FinishSourceMigration(context, AvatarImmigrationState.Completed);
                }

                return true;

            case AvatarMigrationCommand.TargetAborted:
                if (avatar.ImmigrationState == AvatarImmigrationState.RollingBack
                    && response.Success)
                {
                    context.TargetWasAborted = true;
                    SendGateMigrationMessage(context, AvatarMigrationCommand.RollbackRoute);
                }

                return true;

            case AvatarMigrationCommand.RouteRolledBack:
                if (context.SourceSpaceId != Guid.Empty
                    && (!Entities.TryGetValue(context.SourceSpaceId, out var rollbackSourceEntity)
                        || rollbackSourceEntity is not SpaceEntity rollbackSourceSpace
                        || !rollbackSourceSpace.Enter(avatar)))
                {
                    DELogger.Error(
                        nameof(GameServerRuntimeState),
                        $"Failed to restore Avatar to source Space after immigration rollback, avatarId={avatar.Guid}, migrationId={response.MigrationId}, sourceSpaceId={context.SourceSpaceId}."
                    );
                }

                FinishSourceMigration(context, AvatarImmigrationState.Failed);
                return true;

            default:
                return false;
            }
        }

        private bool HandlePrepareTarget(AvatarMigrationMessage request)
        {
            if (!string.Equals(request.TargetGameServerId, _managedRuntimeState.ServerId, StringComparison.Ordinal))
            {
                return false;
            }

            if (_lastAbortedTargetAvatarMigrations.TryGetValue(request.AvatarId, out var abortedMigrationId)
                && abortedMigrationId == request.MigrationId)
            {
                return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "migration was aborted");
            }

            if (_lastCompletedTargetAvatarMigrations.TryGetValue(request.AvatarId, out var completedMigrationId)
                && completedMigrationId == request.MigrationId)
            {
                return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, true);
            }

            if (_targetAvatarMigrations.TryGetValue(request.MigrationId, out var existingContext))
            {
                var isSameMigration = IsSameMigration(existingContext.Message, request);
                return SendTargetResponse(
                    request,
                    AvatarMigrationCommand.TargetPrepared,
                    isSameMigration,
                    isSameMigration ? string.Empty : "migration id collision"
                );
            }

            if (request.TargetSpaceId == Guid.Empty
                || !Entities.TryGetValue(request.TargetSpaceId, out var targetEntity)
                || !(targetEntity is SpaceEntity))
            {
                return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "target Space does not exist on target Game");
            }

            if (Entities.ContainsKey(request.AvatarId))
            {
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
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "failed to construct target Avatar");
                }

                if (!EntitySerializer.TryDeserialize(candidate, EntitySerializeReason.Migrate, request.AvatarData)
                    || candidate.Guid != request.AvatarId)
                {
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetPrepared, false, "invalid Avatar migration snapshot");
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
            if (!_targetAvatarMigrations.TryGetValue(request.MigrationId, out var context)
                || !IsSameMigration(context.Message, request))
            {
                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "target Avatar was not prepared");
            }

            if (context.IsActivated)
            {
                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, true);
            }

            var avatar = context.Avatar;
            if (Avatars.TryGetValue(avatar.Guid, out var existingAvatar)
                && !ReferenceEquals(existingAvatar, avatar))
            {
                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "another Avatar instance is active on target Game");
            }

            try
            {
                RegisterLocalEntity(avatar);
                if (request.ClientSessionId != 0)
                {
                    AvatarClientSessionIds[avatar.Guid] = request.ClientSessionId;
                }

                if (!Entities.TryGetValue(request.TargetSpaceId, out var targetEntity)
                    || targetEntity is not SpaceEntity targetSpace
                    || !targetSpace.Enter(avatar))
                {
                    UnregisterLocalEntity(avatar);
                    return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, "failed to enter target Space");
                }

                context.IsActivated = true;
                avatar.SetImmigrationState(
                    AvatarImmigrationState.TargetActivated,
                    request.MigrationId,
                    new EntityMailBox(request.TargetSpaceId, request.TargetGameServerId),
                    request.SourceGameServerId,
                    request.TargetGameServerId
                );
                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, true);
            }
            catch (Exception exception)
            {
                if (Entities.TryGetValue(request.TargetSpaceId, out var targetEntity)
                    && targetEntity is SpaceEntity targetSpace)
                {
                    targetSpace.Leave(avatar);
                }

                UnregisterLocalEntity(avatar);
                return SendTargetResponse(request, AvatarMigrationCommand.TargetActivated, false, exception.Message);
            }
        }

        private bool HandleCompleteTarget(AvatarMigrationMessage request)
        {
            if (_lastCompletedTargetAvatarMigrations.TryGetValue(request.AvatarId, out var completedMigrationId)
                && completedMigrationId == request.MigrationId)
            {
                return SendTargetResponse(request, AvatarMigrationCommand.TargetCompleted, true);
            }

            if (!_targetAvatarMigrations.TryGetValue(request.MigrationId, out var context)
                || !IsSameMigration(context.Message, request)
                || !context.IsActivated)
            {
                return SendTargetResponse(request, AvatarMigrationCommand.TargetCompleted, false, "target Avatar is not active");
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
            if (_lastCompletedTargetAvatarMigrations.TryGetValue(request.AvatarId, out var completedMigrationId)
                && completedMigrationId == request.MigrationId)
            {
                return SendTargetResponse(request, AvatarMigrationCommand.TargetAborted, false, "target migration is already completed");
            }

            if (_lastAbortedTargetAvatarMigrations.TryGetValue(request.AvatarId, out var abortedMigrationId)
                && abortedMigrationId == request.MigrationId)
            {
                return SendTargetResponse(request, AvatarMigrationCommand.TargetAborted, true);
            }

            _lastAbortedTargetAvatarMigrations[request.AvatarId] = request.MigrationId;
            if (!_targetAvatarMigrations.TryGetValue(request.MigrationId, out var context)
                || !IsSameMigration(context.Message, request))
            {
                return SendTargetResponse(request, AvatarMigrationCommand.TargetAborted, true);
            }

            if (context.IsActivated)
            {
                if (Entities.TryGetValue(request.TargetSpaceId, out var targetEntity)
                    && targetEntity is SpaceEntity targetSpace)
                {
                    targetSpace.Leave(context.Avatar);
                }

                UnregisterLocalEntity(context.Avatar);
            }

            context.Avatar.SetImmigrationState(
                AvatarImmigrationState.Failed,
                request.MigrationId,
                new EntityMailBox(request.TargetSpaceId, request.TargetGameServerId),
                request.SourceGameServerId,
                request.TargetGameServerId
            );
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

        private void RetryAvatarMigration(Guid migrationId)
        {
            if (!_sourceAvatarMigrations.TryGetValue(migrationId, out var context))
            {
                return;
            }

            switch (context.Avatar.ImmigrationState)
            {
            case AvatarImmigrationState.FreezingRoute:
                SendGateMigrationMessage(context, AvatarMigrationCommand.FreezeRoute);
                break;
            case AvatarImmigrationState.PreparingTarget:
                SendTargetMigrationMessage(context, AvatarMigrationCommand.PrepareTarget, true);
                break;
            case AvatarImmigrationState.ActivatingTarget:
                SendTargetMigrationMessage(context, AvatarMigrationCommand.ActivateTarget, false);
                break;
            case AvatarImmigrationState.CommittingRoute:
                SendGateMigrationMessage(context, AvatarMigrationCommand.CommitRoute);
                break;
            case AvatarImmigrationState.CompletingTarget:
                SendTargetMigrationMessage(context, AvatarMigrationCommand.CompleteTarget, false);
                break;
            case AvatarImmigrationState.RollingBack:
                if (context.TargetWasPrepared && !context.TargetWasAborted)
                {
                    SendTargetMigrationMessage(context, AvatarMigrationCommand.AbortTarget, false);
                    break;
                }

                SendGateMigrationMessage(context, AvatarMigrationCommand.RollbackRoute);
                break;
            }
        }

        private bool SendGateMigrationMessage(SourceAvatarMigrationContext context, AvatarMigrationCommand command)
        {
            var message = CopyMessage(context.Message, command);
            var payload = AvatarMigrationProtocol.BuildServerRpcPayload(
                ServerRpcTargetKind.AvatarMigrationGate,
                string.Empty,
                message
            );
            return NativeAPI.SendServerRpcToServer(message.GateServerId, payload);
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
            return NativeAPI.SendServerRpcToServer(message.GateServerId, payload);
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
            return NativeAPI.SendServerRpcToServer(request.GateServerId, payload);
        }

        private void StartAvatarMigrationRetryTimer(SourceAvatarMigrationContext context)
        {
            try
            {
                context.RetryTimerId = DETimer.AddTimer(
                    AvatarMigrationRetryMilliseconds,
                    () => RetryAvatarMigration(context.Message.MigrationId),
                    true
                );
            }
            catch (Exception exception)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Failed to start Avatar immigration retry timer: {exception}");
            }
        }

        private static void CancelAvatarMigrationRetryTimer(SourceAvatarMigrationContext context)
        {
            if (context.RetryTimerId != 0)
            {
                DETimer.CancelTimer(context.RetryTimerId);
                context.RetryTimerId = 0;
            }
        }

        private void FinishSourceMigration(SourceAvatarMigrationContext context, AvatarImmigrationState state)
        {
            CancelAvatarMigrationRetryTimer(context);
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
            return left.MigrationId == right.MigrationId
                && left.AvatarId == right.AvatarId
                && left.TargetSpaceId == right.TargetSpaceId
                && left.ClientSessionId == right.ClientSessionId
                && string.Equals(left.SourceGameServerId, right.SourceGameServerId, StringComparison.Ordinal)
                && string.Equals(left.TargetGameServerId, right.TargetGameServerId, StringComparison.Ordinal)
                && string.Equals(left.GateServerId, right.GateServerId, StringComparison.Ordinal);
        }
    }

    internal enum QueuedAvatarMigrationMessageKind
    {
        AvatarRpcToGame,
        ServerRpcToGame,
        AvatarRpcToClient,
    }

    internal sealed class QueuedAvatarMigrationMessage
    {
        public QueuedAvatarMigrationMessageKind Kind;
        public string SourceServerId = string.Empty;
        public byte[] Payload = Array.Empty<byte>();
    }

    internal sealed class GateAvatarMigrationContext
    {
        public AvatarMigrationMessage Message;
        public readonly Queue<QueuedAvatarMigrationMessage> Messages = new Queue<QueuedAvatarMigrationMessage>();
    }

    public sealed partial class GateServerRuntimeState
    {
        private readonly Dictionary<Guid, GateAvatarMigrationContext> _gateAvatarMigrations =
            new Dictionary<Guid, GateAvatarMigrationContext>();

        internal bool HandleAvatarMigrationGateMessage(string sourceServerId, ServerRpcPayload rpc)
        {
            if (!AvatarMigrationProtocol.TryDeserialize(rpc, out var message)
                || !string.Equals(sourceServerId, message.SourceGameServerId, StringComparison.Ordinal)
                || !string.Equals(message.GateServerId, _managedRuntimeState.ServerId, StringComparison.Ordinal))
            {
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
            if (!AvatarIdToAccount.TryGetValue(request.AvatarId, out var account))
            {
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
                return SendGateResponse(
                    request,
                    AvatarMigrationCommand.RouteFrozen,
                    isSameMigration,
                    isSameMigration ? string.Empty : "another migration is active"
                );
            }

            if (!string.Equals(account.GameServerId, request.SourceGameServerId, StringComparison.Ordinal))
            {
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
            if (!AvatarIdToAccount.TryGetValue(request.AvatarId, out var account))
            {
                return SendGateResponse(request, AvatarMigrationCommand.RouteCommitted, false, "Avatar route does not exist");
            }

            if (account.LastMigrationId == request.MigrationId && account.LastMigrationCommitted)
            {
                return SendGateResponse(request, AvatarMigrationCommand.RouteCommitted, true);
            }

            if (!_gateAvatarMigrations.TryGetValue(request.AvatarId, out var context)
                || !GameServerRuntimeState.IsSameMigration(context.Message, request))
            {
                return SendGateResponse(request, AvatarMigrationCommand.RouteCommitted, false, "migration route is not frozen");
            }

            if (!FlushGateQueue(context, true, account.ClientSessionId))
            {
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
            if (!AvatarIdToAccount.TryGetValue(request.AvatarId, out var account))
            {
                return SendGateResponse(request, AvatarMigrationCommand.RouteRolledBack, false, "Avatar route does not exist");
            }

            if (account.LastMigrationId == request.MigrationId && !account.LastMigrationCommitted)
            {
                return SendGateResponse(request, AvatarMigrationCommand.RouteRolledBack, true);
            }

            if (!_gateAvatarMigrations.TryGetValue(request.AvatarId, out var context)
                || !GameServerRuntimeState.IsSameMigration(context.Message, request))
            {
                return SendGateResponse(request, AvatarMigrationCommand.RouteRolledBack, false, "migration route is not frozen");
            }

            if (!FlushGateQueue(context, false, account.ClientSessionId))
            {
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
            while (context.Messages.Count > 0)
            {
                var message = context.Messages.Peek();
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
                        context.Messages.Dequeue();
                        continue;
                    }

                    if (!commit
                        && !string.Equals(message.SourceServerId, context.Message.SourceGameServerId, StringComparison.Ordinal))
                    {
                        context.Messages.Dequeue();
                        continue;
                    }

                    sent = NativeAPI.SendAvatarRpcToClient(clientSessionId, message.Payload);
                    break;
                default:
                    return false;
                }

                if (!sent)
                {
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
            return NativeAPI.SendServerRpcToServer(request.SourceGameServerId, payload);
        }
    }
}
