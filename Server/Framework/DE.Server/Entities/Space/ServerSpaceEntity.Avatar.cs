using System;
using System.Collections.Generic;
using DE.Server.NativeBridge;

namespace DE.Server.Entities
{
    public partial class ServerSpaceEntity
    {
        private readonly Dictionary<Guid, AvatarEntity> _avatars = new Dictionary<Guid, AvatarEntity>();

        public IReadOnlyDictionary<Guid, AvatarEntity> Avatars => _avatars;

        public bool Enter(AvatarEntity avatar)
        {
            if (avatar == null)
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Enter rejected because Avatar is null, spaceId={Guid}.");
                return false;
            }

            if (avatar.Guid == Guid.Empty)
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Enter rejected because Avatar Guid is empty, spaceId={Guid}.");
                return false;
            }

            if (Guid == Guid.Empty)
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Enter rejected because Space Guid is empty, avatarId={avatar.Guid}.");
                return false;
            }

            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (!runtime.Entities.TryGetValue(Guid, out var registeredSpace))
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Enter rejected because Space is not registered on the current Game, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredSpace, this))
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Enter rejected because another Entity instance is registered with the same Space id, avatarId={avatar.Guid}, spaceId={Guid}, registeredType={registeredSpace?.GetType().FullName ?? "<null>"}.");
                return false;
            }

            if (!runtime.Avatars.TryGetValue(avatar.Guid, out var registeredAvatar))
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Enter rejected because Avatar is not registered on the current Game, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredAvatar, avatar))
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Enter rejected because another Avatar instance is registered with the same id, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (_avatars.TryGetValue(avatar.Guid, out var existingAvatar))
            {
                if (!ReferenceEquals(existingAvatar, avatar))
                {
                    DELogger.Error(nameof(ServerSpaceEntity), $"Space Enter rejected because the Space contains another Avatar instance with the same id, avatarId={avatar.Guid}, spaceId={Guid}.");
                    return false;
                }

                if (!ReferenceEquals(avatar.CurrentSpace, this))
                {
                    DELogger.Error(nameof(ServerSpaceEntity), "Space Enter rejected because Avatar membership and CurrentSpace are inconsistent, "
                        + $"avatarId={avatar.Guid}, spaceId={Guid}, currentSpaceId={avatar.CurrentSpace?.Guid ?? Guid.Empty}.");
                    return false;
                }

                DELogger.Error(nameof(ServerSpaceEntity), $"Space Enter rejected because Avatar is already in the Space, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (avatar.CurrentSpace != null)
            {
                DELogger.Error(nameof(ServerSpaceEntity), "Space Enter rejected because Avatar must explicitly leave its current Space first, "
                    + $"avatarId={avatar.Guid}, targetSpaceId={Guid}, currentSpaceId={avatar.CurrentSpace.Guid}.");
                return false;
            }

            if (MaxPlayers > 0 && _avatars.Count >= MaxPlayers)
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Enter rejected due to capacity, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }
            _avatars.Add(avatar.Guid, avatar);
            avatar.CurrentSpace = this;
            avatar.Space.SetSpace(Guid, SpaceNo);
            try
            {
                avatar.OnEnterSpace(this);
            }
            catch (Exception exception)
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"OnEnterSpace failed, avatarId={avatar.Guid}, spaceId={Guid}: {exception}");
            }

            // A migration candidate becomes visible only after Gate has committed its route.
            if (avatar.ImmigrationState != AvatarImmigrationState.TargetPrepared)
            {
                CompleteAvatarEntry(avatar);
            }
            return true;
        }

        public bool Leave(AvatarEntity avatar)
        {
            if (avatar == null)
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Leave rejected because Avatar is null, spaceId={Guid}.");
                return false;
            }

            if (avatar.Guid == Guid.Empty)
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Leave rejected because Avatar Guid is empty, spaceId={Guid}.");
                return false;
            }

            if (Guid == Guid.Empty)
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Leave rejected because Space Guid is empty, avatarId={avatar.Guid}.");
                return false;
            }

            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (!runtime.Entities.TryGetValue(Guid, out var registeredSpace))
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Leave rejected because Space is not registered on the current Game, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredSpace, this))
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Leave rejected because another Entity instance is registered with the same Space id, avatarId={avatar.Guid}, spaceId={Guid}, registeredType={registeredSpace?.GetType().FullName ?? "<null>"}.");
                return false;
            }

            if (!runtime.Avatars.TryGetValue(avatar.Guid, out var registeredAvatar))
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Leave rejected because Avatar is not registered on the current Game, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredAvatar, avatar))
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Leave rejected because another Avatar instance is registered with the same id, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (!_avatars.TryGetValue(avatar.Guid, out var existingAvatar))
            {
                DELogger.Error(nameof(ServerSpaceEntity), "Space Leave rejected because Avatar is not in the Space, "
                    + $"avatarId={avatar.Guid}, spaceId={Guid}, currentSpaceId={avatar.CurrentSpace?.Guid ?? Guid.Empty}.");
                return false;
            }

            if (!ReferenceEquals(existingAvatar, avatar))
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Leave rejected because the Space contains another Avatar instance with the same id, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(avatar.CurrentSpace, this))
            {
                DELogger.Error(nameof(ServerSpaceEntity), "Space Leave rejected because Avatar CurrentSpace points to another Space, "
                    + $"avatarId={avatar.Guid}, spaceId={Guid}, currentSpaceId={avatar.CurrentSpace?.Guid ?? Guid.Empty}.");
                return false;
            }

            if (!_avatars.Remove(avatar.Guid))
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Space Leave failed to remove Avatar after membership validation, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            avatar.CurrentSpace = null;
            RemoveVisibleEntity(avatar);
            try
            {
                avatar.OnLeaveSpace(this);
            }
            catch (Exception exception)
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"OnLeaveSpace failed, avatarId={avatar.Guid}, spaceId={Guid}: {exception}");
            }

            return true;
        }
    }
}
