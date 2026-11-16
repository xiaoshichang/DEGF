using System.Linq;
using DE.Server.NativeBridge;

namespace DE.Server.Entities
{
    public partial class ServerSpaceEntity
    {
        private ulong _propertySynchronizationTimer;

        private void StartPropertySynchronization()
        {
            _propertySynchronizationTimer = DETimer.AddTimer(50, SynchronizeMemberProperties, true);
        }

        private void StopPropertySynchronization()
        {
            if (_propertySynchronizationTimer != 0)
            {
                DETimer.CancelTimer(_propertySynchronizationTimer);
                _propertySynchronizationTimer = 0;
            }
        }

        private void SynchronizeMemberProperties()
        {
            if (!IsInitialized)
            {
                return;
            }
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            runtime.RetryPendingSpaceEntries(this);
            foreach (var avatar in AvatarManageCmpt.Avatars.Values.ToArray())
            {
                if (avatar.AutoSyncProperty && CanSynchronizeAvatar(avatar))
                {
                    avatar.SynchronizeProperties(runtime);
                }
            }
        }
    }
}
