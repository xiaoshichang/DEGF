using System;
using System.Collections.Generic;
using DE.Server.NativeBridge;

namespace DE.Server.Entities
{
    public class ServerSpaceAvatarManageComponent : ServerEntityComponent
    {
        private ServerSpaceEntity Space => (ServerSpaceEntity)OwnerEntity;
        private Guid Guid => Space.Guid;

        /// <summary>初始化本地成员集合，不依赖其他组件。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            _avatars = new Dictionary<Guid, ServerAvatarEntity>();
        }

        /// <summary>组件依赖清理后释放成员集合字段。</summary>
        protected override void PostUnInitialize()
        {
            _avatars = null;
            base.PostUnInitialize();
        }

        /// <summary>解除 Avatar 与当前空间的关联并清空成员集合。</summary>
        protected override void UnInitialize()
        {
            if (_avatars != null)
            {
                foreach (var avatar in _avatars.Values)
                {
                    if (!ReferenceEquals(avatar.SpaceCmpt.CurrentSpace, Space))
                    {
                        DELogger.Error("unknown error.");
                    }
                    avatar.SpaceCmpt.CurrentSpace = null;
                }
                _avatars.Clear();
            }
            base.UnInitialize();
        }

        private Dictionary<Guid, ServerAvatarEntity> _avatars;

        public IReadOnlyDictionary<Guid, ServerAvatarEntity> Avatars => _avatars;

        public bool Enter(ServerAvatarEntity avatar)
        {
            if (avatar == null)
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent), $"Space Enter rejected because Avatar is null, spaceId={Guid}.");
                return false;
            }

            if (avatar.Guid == Guid.Empty)
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent), $"Space Enter rejected because Avatar Guid is empty, spaceId={Guid}.");
                return false;
            }

            if (Guid == Guid.Empty)
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent), $"Space Enter rejected because Space Guid is empty, avatarId={avatar.Guid}.");
                return false;
            }

            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (!runtime.Entities.TryGetValue(Guid, out var registeredSpace))
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                    $"Space Enter rejected because Space is not registered on the current Game, avatarId={avatar.Guid}, "
                    + $"spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredSpace, Space))
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                    $"Space Enter rejected because another Entity instance is registered with the same Space id, "
                    + $"avatarId={avatar.Guid}, spaceId={Guid}, registeredType={registeredSpace?.GetType().FullName ?? "<null>"}.");
                return false;
            }

            if (!runtime.Avatars.TryGetValue(avatar.Guid, out var registeredAvatar))
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                    $"Space Enter rejected because Avatar is not registered on the current Game, avatarId={avatar.Guid}, "
                    + $"spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredAvatar, avatar))
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                    $"Space Enter rejected because another Avatar instance is registered with the same id, avatarId={avatar.Guid}, "
                    + $"spaceId={Guid}.");
                return false;
            }

            if (_avatars.TryGetValue(avatar.Guid, out var existingAvatar))
            {
                if (!ReferenceEquals(existingAvatar, avatar))
                {
                    DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                        $"Space Enter rejected because the Space contains another Avatar instance with the same id, "
                        + $"avatarId={avatar.Guid}, spaceId={Guid}.");
                    return false;
                }

                if (!ReferenceEquals(avatar.SpaceCmpt.CurrentSpace, Space))
                {
                    DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                        "Space Enter rejected because Avatar membership and CurrentSpace are inconsistent, "
                        + $"avatarId={avatar.Guid}, spaceId={Guid}, currentSpaceId={avatar.SpaceCmpt.CurrentSpace?.Guid ?? Guid.Empty}.");
                    return false;
                }

                DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                    $"Space Enter rejected because Avatar is already in the Space, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (avatar.SpaceCmpt.CurrentSpace != null)
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent), "Space Enter rejected because Avatar must explicitly leave its current Space first, "
                    + $"avatarId={avatar.Guid}, targetSpaceId={Guid}, currentSpaceId={avatar.SpaceCmpt.CurrentSpace.Guid}.");
                return false;
            }

            if (Space.MaxPlayers > 0 && _avatars.Count >= Space.MaxPlayers)
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent), $"Space Enter rejected due to capacity, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }
            avatar.SpaceCmpt.OnBeforeEnterSpace(Space);
            var previousSpaceId = avatar.SpaceCmpt.SpaceId;
            var previousSpaceNo = avatar.SpaceCmpt.SpaceNo;
            _avatars.Add(avatar.Guid, avatar);
            avatar.SpaceCmpt.CurrentSpace = Space;
            avatar.SpaceCmpt.SetSpace(Guid, Space.SpaceNo);
            // Migration activation reserves membership; AOI entry waits for the Gate route commit.
            if (avatar.ImmigateCmpt.ImmigrationState == AvatarImmigrationState.TargetPrepared)
            {
                return true;
            }
            if (!Space.AOISpace.AddAOIAgent(avatar))
            {
                _avatars.Remove(avatar.Guid);
                avatar.SpaceCmpt.CurrentSpace = null;
                avatar.SpaceCmpt.SetSpace(previousSpaceId, previousSpaceNo);
                return false;
            }
            avatar.SpaceCmpt.OnAfterEnterSpace(Space);
            return true;
        }

        public bool Leave(ServerAvatarEntity avatar)
        {
            if (avatar == null)
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent), $"Space Leave rejected because Avatar is null, spaceId={Guid}.");
                return false;
            }

            if (avatar.Guid == Guid.Empty)
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent), $"Space Leave rejected because Avatar Guid is empty, spaceId={Guid}.");
                return false;
            }

            if (Guid == Guid.Empty)
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent), $"Space Leave rejected because Space Guid is empty, avatarId={avatar.Guid}.");
                return false;
            }

            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (!runtime.Entities.TryGetValue(Guid, out var registeredSpace))
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                    $"Space Leave rejected because Space is not registered on the current Game, avatarId={avatar.Guid}, "
                    + $"spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredSpace, Space))
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                    $"Space Leave rejected because another Entity instance is registered with the same Space id, "
                    + $"avatarId={avatar.Guid}, spaceId={Guid}, registeredType={registeredSpace?.GetType().FullName ?? "<null>"}.");
                return false;
            }

            if (!runtime.Avatars.TryGetValue(avatar.Guid, out var registeredAvatar))
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                    $"Space Leave rejected because Avatar is not registered on the current Game, avatarId={avatar.Guid}, "
                    + $"spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(registeredAvatar, avatar))
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                    $"Space Leave rejected because another Avatar instance is registered with the same id, avatarId={avatar.Guid}, "
                    + $"spaceId={Guid}.");
                return false;
            }

            if (!_avatars.TryGetValue(avatar.Guid, out var existingAvatar))
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent), "Space Leave rejected because Avatar is not in the Space, "
                    + $"avatarId={avatar.Guid}, spaceId={Guid}, currentSpaceId={avatar.SpaceCmpt.CurrentSpace?.Guid ?? Guid.Empty}.");
                return false;
            }

            if (!ReferenceEquals(existingAvatar, avatar))
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                    $"Space Leave rejected because the Space contains another Avatar instance with the same id, "
                    + $"avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            if (!ReferenceEquals(avatar.SpaceCmpt.CurrentSpace, Space))
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent), "Space Leave rejected because Avatar CurrentSpace points to another Space, "
                    + $"avatarId={avatar.Guid}, spaceId={Guid}, currentSpaceId={avatar.SpaceCmpt.CurrentSpace?.Guid ?? Guid.Empty}.");
                return false;
            }

            avatar.SpaceCmpt.OnBeforeLeaveSpace(Space);
            if (!_avatars.Remove(avatar.Guid))
            {
                DELogger.Error(nameof(ServerSpaceAvatarManageComponent),
                    $"Space Leave failed to remove Avatar after membership validation, avatarId={avatar.Guid}, spaceId={Guid}.");
                return false;
            }

            avatar.SpaceCmpt.CurrentSpace = null;
            runtime.CancelSpaceEntryNotification(avatar);
            Space.AOISpace.RemoveAOIAgent(avatar);
            avatar.SpaceCmpt.OnAfterLeaveSpace(Space);

            return true;
        }
    }
}
