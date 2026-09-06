using System;
using DE.Server.Entities;
using DE.Share.Rpc;

namespace DE.Server.NativeBridge
{
    public sealed partial class GameServerRuntimeState
    {
        public bool Teleport(AvatarEntity avatar, EntityMailBox space)
        {
            if (avatar == null)
            {
                DELogger.Error(nameof(GameServerRuntimeState), "Avatar teleport rejected because Avatar is null.");
                return false;
            }

            if (avatar.Guid == Guid.Empty)
            {
                DELogger.Error(nameof(GameServerRuntimeState), "Avatar teleport rejected because Avatar Guid is empty.");
                return false;
            }

            if (!space.IsValid)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar teleport rejected because target Space MailBox is invalid, avatarId={avatar.Guid}, targetSpaceId={space.EntityId}, targetGameServerId={space.BindingGame}.");
                return false;
            }

            if (!Avatars.TryGetValue(avatar.Guid, out var registeredAvatar))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar teleport rejected because Avatar is not registered on the current Game, avatarId={avatar.Guid}, targetSpaceId={space.EntityId}.");
                return false;
            }

            if (!ReferenceEquals(registeredAvatar, avatar))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Avatar teleport rejected because another Avatar instance is registered with the same id, avatarId={avatar.Guid}, targetSpaceId={space.EntityId}.");
                return false;
            }

            if (!string.Equals(space.BindingGame, _managedRuntimeState.ServerId, StringComparison.Ordinal))
            {
                return BeginAvatarImmigration(avatar, space);
            }

            switch (avatar.ImmigrationState)
            {
            case AvatarImmigrationState.Idle:
            case AvatarImmigrationState.Completed:
            case AvatarImmigrationState.Failed:
                break;
            default:
                DELogger.Error(nameof(GameServerRuntimeState), $"Local Avatar teleport rejected because Avatar immigration is in progress, avatarId={avatar.Guid}, targetSpaceId={space.EntityId}, state={avatar.ImmigrationState}.");
                return false;
            }

            if (!Entities.TryGetValue(space.EntityId, out var targetEntity))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Local Avatar teleport rejected because target Space does not exist, avatarId={avatar.Guid}, targetSpaceId={space.EntityId}.");
                return false;
            }

            if (targetEntity is not SpaceEntity targetSpace)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Local Avatar teleport rejected because target Entity is not a Space, avatarId={avatar.Guid}, targetSpaceId={space.EntityId}, targetEntityType={targetEntity?.GetType().FullName ?? "<null>"}.");
                return false;
            }

            if (avatar.CurrentSpaceId == targetSpace.Guid)
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Local Avatar teleport rejected because Avatar is already in the target Space, avatarId={avatar.Guid}, targetSpaceId={space.EntityId}.");
                return false;
            }

            if (avatar.CurrentSpaceId != Guid.Empty)
            {
                var currentSpaceId = avatar.CurrentSpaceId;
                if (!Entities.TryGetValue(currentSpaceId, out var currentEntity))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Local Avatar teleport rejected because current Space does not exist, avatarId={avatar.Guid}, currentSpaceId={currentSpaceId}, targetSpaceId={space.EntityId}.");
                    return false;
                }

                if (currentEntity is not SpaceEntity currentSpace)
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Local Avatar teleport rejected because current Entity is not a Space, avatarId={avatar.Guid}, currentSpaceId={currentSpaceId}, targetSpaceId={space.EntityId}, currentEntityType={currentEntity?.GetType().FullName ?? "<null>"}.");
                    return false;
                }

                if (!currentSpace.Leave(avatar))
                {
                    DELogger.Error(nameof(GameServerRuntimeState), $"Local Avatar teleport failed because Avatar could not leave its current Space, avatarId={avatar.Guid}, currentSpaceId={currentSpaceId}, targetSpaceId={space.EntityId}.");
                    return false;
                }
            }

            if (!targetSpace.Enter(avatar))
            {
                DELogger.Error(nameof(GameServerRuntimeState), $"Local Avatar teleport failed because Avatar could not enter target Space, avatarId={avatar.Guid}, targetSpaceId={space.EntityId}.");
                return false;
            }

            return true;
        }
    }
}
