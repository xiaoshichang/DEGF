using System;
using DE.Server.NativeBridge;
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

    public partial class ServerAvatarImmigateComponent : ServerEntityComponent
    {
        public AvatarImmigrationState ImmigrationState { get; private set; }
        public Guid ImmigrationId { get; private set; }
        public EntityMailBox ImmigrationTargetSpace { get; private set; }

        // Source-side completion continues after the entity and its components have been detached.
        private Guid Guid;

        /// <summary>记录实体身份，供源实体清理后继续处理迁移协议终态。</summary>
        protected override void Initialize()
        {
            Guid = OwnerEntity.Guid;
            base.Initialize();
        }

        internal void SetImmigrationState(
            AvatarImmigrationState state,
            Guid migrationId,
            EntityMailBox targetSpace,
            string sourceGameServerId,
            string targetGameServerId)
        {
            if (!IsValidImmigrationStateTransition(ImmigrationState, state))
            {
                var error = $"Invalid Avatar immigration state transition, avatarId={Guid}, migrationId={migrationId}, "
                    + $"currentState={ImmigrationState}, targetState={state}.";
                DELogger.Error(nameof(ServerAvatarEntity), error);
                throw new InvalidOperationException(error);
            }

            if (ImmigrationState != AvatarImmigrationState.Idle)
            {
                if (ImmigrationId != migrationId)
                {
                    var error = $"Avatar immigration state transition rejected because migration id changed, avatarId={Guid}, "
                        + $"currentMigrationId={ImmigrationId}, incomingMigrationId={migrationId}, currentState={ImmigrationState}, targetState={state}.";
                    DELogger.Error(nameof(ServerAvatarEntity), error);
                    throw new InvalidOperationException(error);
                }

                if (ImmigrationTargetSpace.EntityId != targetSpace.EntityId)
                {
                    var error = $"Avatar immigration state transition rejected because target Space id changed, avatarId={Guid}, "
                        + $"migrationId={migrationId}, currentTargetSpaceId={ImmigrationTargetSpace.EntityId}, "
                        + $"incomingTargetSpaceId={targetSpace.EntityId}.";
                    DELogger.Error(nameof(ServerAvatarEntity), error);
                    throw new InvalidOperationException(error);
                }

                if (!string.Equals(ImmigrationTargetSpace.BindingGame, targetSpace.BindingGame, StringComparison.Ordinal))
                {
                    var error = $"Avatar immigration state transition rejected because target Game id changed, avatarId={Guid}, "
                        + $"migrationId={migrationId}, currentTargetGameServerId={ImmigrationTargetSpace.BindingGame}, "
                        + $"incomingTargetGameServerId={targetSpace.BindingGame}.";
                    DELogger.Error(nameof(ServerAvatarEntity), error);
                    throw new InvalidOperationException(error);
                }
            }

            ImmigrationState = state;
            ImmigrationId = migrationId;
            ImmigrationTargetSpace = targetSpace;
            DELogger.Info(
                nameof(ServerAvatarEntity),
                $"Avatar immigration state changed, avatarId={Guid}, migrationId={migrationId}, state={state}, "
                    + $"sourceGameServerId={sourceGameServerId}, targetGameServerId={targetGameServerId}, targetSpaceId={targetSpace.EntityId}."
            );
        }

        private static bool IsValidImmigrationStateTransition(
            AvatarImmigrationState currentState,
            AvatarImmigrationState targetState)
        {
            AvatarImmigrationState nextState;
            switch (currentState)
            {
            case AvatarImmigrationState.Idle:
                nextState = AvatarImmigrationState.FreezingRoute;
                break;
            case AvatarImmigrationState.FreezingRoute:
                nextState = AvatarImmigrationState.Serializing;
                break;
            case AvatarImmigrationState.Serializing:
                nextState = AvatarImmigrationState.PreparingTarget;
                break;
            case AvatarImmigrationState.PreparingTarget:
                nextState = AvatarImmigrationState.TargetPrepared;
                break;
            case AvatarImmigrationState.TargetPrepared:
                nextState = AvatarImmigrationState.ActivatingTarget;
                break;
            case AvatarImmigrationState.ActivatingTarget:
                nextState = AvatarImmigrationState.TargetActivated;
                break;
            case AvatarImmigrationState.TargetActivated:
                nextState = AvatarImmigrationState.CommittingRoute;
                break;
            case AvatarImmigrationState.CommittingRoute:
                nextState = AvatarImmigrationState.CompletingTarget;
                break;
            case AvatarImmigrationState.CompletingTarget:
                nextState = AvatarImmigrationState.Completed;
                break;
            case AvatarImmigrationState.RollingBack:
                nextState = AvatarImmigrationState.Failed;
                break;
            default:
                return false;
            }

            if (targetState == nextState)
            {
                return true;
            }

            switch (currentState)
            {
            case AvatarImmigrationState.Idle:
                return targetState == AvatarImmigrationState.TargetPrepared;
            case AvatarImmigrationState.FreezingRoute:
                return targetState == AvatarImmigrationState.Failed;
            case AvatarImmigrationState.Serializing:
            case AvatarImmigrationState.PreparingTarget:
            case AvatarImmigrationState.ActivatingTarget:
            case AvatarImmigrationState.CommittingRoute:
                return targetState == AvatarImmigrationState.RollingBack;
            case AvatarImmigrationState.TargetPrepared:
                if (targetState == AvatarImmigrationState.TargetActivated)
                {
                    return true;
                }

                return targetState == AvatarImmigrationState.Failed;
            case AvatarImmigrationState.TargetActivated:
                if (targetState == AvatarImmigrationState.Completed)
                {
                    return true;
                }

                return targetState == AvatarImmigrationState.Failed;
            default:
                return false;
            }
        }

    }
}
