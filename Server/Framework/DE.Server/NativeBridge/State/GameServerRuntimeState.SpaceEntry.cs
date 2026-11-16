using System;
using System.Collections.Generic;
using System.Linq;
using DE.Server.Entities;

namespace DE.Server.NativeBridge
{
    public sealed partial class GameServerRuntimeState
    {
        private readonly Dictionary<Guid, (ServerAvatarEntity Avatar, ServerSpaceEntity Space)> _pendingSpaceEntryNotifications = new();

        internal void RetryPendingSpaceEntries()
        {
            RetrySourceRollbackEntries();
            RetrySpaceEntryNotifications();
        }

        internal void NotifySpaceEntry(ServerAvatarEntity avatar, ServerSpaceEntity space)
        {
            if (!IsSpaceEntryNotificationCurrent(avatar, space))
            {
                _pendingSpaceEntryNotifications.Remove(avatar.Guid);
                return;
            }

            bool alreadyPending = _pendingSpaceEntryNotifications.TryGetValue(avatar.Guid, out var pending)
                && ReferenceEquals(pending.Avatar, avatar) && ReferenceEquals(pending.Space, space);
            try
            {
                if (avatar.SpaceCmpt.TryNotifySpaceEntry(space))
                {
                    _pendingSpaceEntryNotifications.Remove(avatar.Guid);
                    return;
                }
                if (!alreadyPending)
                {
                    DELogger.Warn(nameof(GameServerRuntimeState),
                        $"Space entry notification queued for retry, avatarId={avatar.Guid}, spaceId={space.Guid}.");
                }
            }
            catch (Exception exception)
            {
                if (!alreadyPending)
                {
                    DELogger.Error(nameof(GameServerRuntimeState),
                        $"Space entry snapshot failed; queued for retry, avatarId={avatar.Guid}, spaceId={space.Guid}: {exception}");
                }
            }
            _pendingSpaceEntryNotifications[avatar.Guid] = (avatar, space);
        }

        internal void CancelSpaceEntryNotification(ServerEntity entity)
        {
            foreach (var avatarId in _pendingSpaceEntryNotifications.Where(pair => ReferenceEquals(pair.Value.Avatar, entity)
                || ReferenceEquals(pair.Value.Space, entity)).Select(pair => pair.Key).ToArray())
            {
                _pendingSpaceEntryNotifications.Remove(avatarId);
            }
        }

        private bool IsSpaceEntryNotificationCurrent(ServerAvatarEntity avatar, ServerSpaceEntity space)
        {
            return avatar != null && space != null && avatar.IsInitialized && space.IsInitialized
                && ReferenceEquals(avatar.SpaceCmpt.CurrentSpace, space)
                && Avatars.TryGetValue(avatar.Guid, out var registeredAvatar) && ReferenceEquals(registeredAvatar, avatar)
                && Entities.TryGetValue(space.Guid, out var registeredSpace) && ReferenceEquals(registeredSpace, space)
                && AvatarClientSessionIds.TryGetValue(avatar.Guid, out var sessionId) && sessionId != 0;
        }

        private void RetrySpaceEntryNotifications()
        {
            foreach (var pending in _pendingSpaceEntryNotifications.Values.ToArray())
            {
                if (!IsSpaceEntryNotificationCurrent(pending.Avatar, pending.Space))
                {
                    _pendingSpaceEntryNotifications.Remove(pending.Avatar.Guid);
                }
                else if (ServerEntity.CanSynchronizeAvatar(pending.Avatar))
                {
                    // Retry only the framework notification, never the business OnAfterEnterSpace callback.
                    NotifySpaceEntry(pending.Avatar, pending.Space);
                }
            }
        }

    }
}
