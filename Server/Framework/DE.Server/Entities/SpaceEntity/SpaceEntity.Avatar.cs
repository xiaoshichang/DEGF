using System;
using System.Collections.Generic;
using DE.Server.NativeBridge;

namespace DE.Server.Entities
{
    public partial class SpaceEntity
    {
        private readonly Dictionary<Guid, AvatarEntity> _avatars = new Dictionary<Guid, AvatarEntity>();

        public IReadOnlyDictionary<Guid, AvatarEntity> Avatars => _avatars;

        public bool Enter(AvatarEntity avatar)
        {
            if (avatar == null)
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Enter rejected because Avatar is null, spaceId={Guid}.");
                return false;
            }

            if (avatar.Guid == Guid.Empty)
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Enter rejected because Avatar Guid is empty, spaceId={Guid}.");
                return false;
            }

            if (Guid == Guid.Empty)
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Enter rejected because Space Guid is empty, avatarId={avatar.Guid}.");
                return false;
            }

            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (!runtime.Entities.TryGetValue(Guid, out var registeredSpace))
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Enter rejected because Space is not registered on the current Game, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredSpace, this))
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Enter rejected because another Entity instance is registered with the same Space id, avatarId={avatar.Guid}, spaceId={Guid}, registeredType={registeredSpace?.GetType().FullName ?? "<null>"}.");
                return false;
            }

            if (!runtime.Avatars.TryGetValue(avatar.Guid, out var registeredAvatar))
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Enter rejected because Avatar is not registered on the current Game, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredAvatar, avatar))
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Enter rejected because another Avatar instance is registered with the same id, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (_avatars.TryGetValue(avatar.Guid, out var existingAvatar))
            {
                if (!ReferenceEquals(existingAvatar, avatar))
                {
                    DELogger.Error(nameof(SpaceEntity), $"Space Enter rejected because the Space contains another Avatar instance with the same id, avatarId={avatar.Guid}, spaceId={Guid}.");
                    return false;
                }

                if (avatar.CurrentSpaceId != Guid)
                {
                    DELogger.Error(nameof(SpaceEntity), $"Space Enter rejected because Avatar membership and CurrentSpaceId are inconsistent, avatarId={avatar.Guid}, spaceId={Guid}, currentSpaceId={avatar.CurrentSpaceId}.");
                    return false;
                }

                return true;
            }

            if (avatar.CurrentSpaceId != Guid.Empty)
            {
                if (!runtime.Entities.TryGetValue(avatar.CurrentSpaceId, out var currentEntity))
                {
                    DELogger.Error(nameof(SpaceEntity), $"Space Enter rejected because Avatar CurrentSpaceId does not resolve to a local Entity, avatarId={avatar.Guid}, targetSpaceId={Guid}, currentSpaceId={avatar.CurrentSpaceId}.");
                    return false;
                }

                if (currentEntity is not SpaceEntity currentSpace)
                {
                    DELogger.Error(nameof(SpaceEntity), $"Space Enter rejected because Avatar CurrentSpaceId resolves to a non-Space Entity, avatarId={avatar.Guid}, targetSpaceId={Guid}, currentSpaceId={avatar.CurrentSpaceId}, currentEntityType={currentEntity?.GetType().FullName ?? "<null>"}.");
                    return false;
                }

                if (!currentSpace.Leave(avatar))
                {
                    DELogger.Error(nameof(SpaceEntity), $"Space Enter rejected because Avatar could not leave its current Space, avatarId={avatar.Guid}, targetSpaceId={Guid}, currentSpaceId={avatar.CurrentSpaceId}.");
                    return false;
                }
            }

            _avatars.Add(avatar.Guid, avatar);
            avatar.CurrentSpaceId = Guid;
            try
            {
                avatar.OnEnterSpace(this);
            }
            catch (Exception exception)
            {
                DELogger.Error(nameof(SpaceEntity), $"OnEnterSpace failed, avatarId={avatar.Guid}, spaceId={Guid}: {exception}");
            }

            return true;
        }

        public bool Leave(AvatarEntity avatar)
        {
            if (avatar == null)
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Leave rejected because Avatar is null, spaceId={Guid}.");
                return false;
            }

            if (avatar.Guid == Guid.Empty)
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Leave rejected because Avatar Guid is empty, spaceId={Guid}.");
                return false;
            }

            if (Guid == Guid.Empty)
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Leave rejected because Space Guid is empty, avatarId={avatar.Guid}.");
                return false;
            }

            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (!runtime.Entities.TryGetValue(Guid, out var registeredSpace))
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Leave rejected because Space is not registered on the current Game, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredSpace, this))
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Leave rejected because another Entity instance is registered with the same Space id, avatarId={avatar.Guid}, spaceId={Guid}, registeredType={registeredSpace?.GetType().FullName ?? "<null>"}.");
                return false;
            }

            if (!runtime.Avatars.TryGetValue(avatar.Guid, out var registeredAvatar))
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Leave rejected because Avatar is not registered on the current Game, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredAvatar, avatar))
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Leave rejected because another Avatar instance is registered with the same id, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (!_avatars.TryGetValue(avatar.Guid, out var existingAvatar))
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Leave rejected because Avatar is not in the Space, avatarId={avatar.Guid}, spaceId={Guid}, currentSpaceId={avatar.CurrentSpaceId}.");
                return false;
            }

            if (!ReferenceEquals(existingAvatar, avatar))
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Leave rejected because the Space contains another Avatar instance with the same id, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (avatar.CurrentSpaceId != Guid)
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Leave rejected because Avatar CurrentSpaceId points to another Space, avatarId={avatar.Guid}, spaceId={Guid}, currentSpaceId={avatar.CurrentSpaceId}.");
                return false;
            }

            if (!_avatars.Remove(avatar.Guid))
            {
                DELogger.Error(nameof(SpaceEntity), $"Space Leave failed to remove Avatar after membership validation, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            avatar.CurrentSpaceId = Guid.Empty;
            try
            {
                avatar.OnLeaveSpace(this);
            }
            catch (Exception exception)
            {
                DELogger.Error(nameof(SpaceEntity), $"OnLeaveSpace failed, avatarId={avatar.Guid}, spaceId={Guid}: {exception}");
            }

            return true;
        }
    }
}
