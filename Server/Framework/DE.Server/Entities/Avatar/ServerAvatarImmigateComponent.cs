using System;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    /// <summary>Each state maps to a KBEngine 2.5.12 entity role or flag; see the migration design document.</summary>
    public enum AvatarImmigrationState
    {
        /// <summary>Entity::isReal(): this process runs authoritative entity logic.</summary>
        Real = 0,
        /// <summary>Entity::changeToGhost(): forward calls to realCell without executing source logic.</summary>
        Ghost = 1,
        /// <summary>Target reqTeleportToCellApp sets TELEPORT_START until reqTeleportToCellAppOver.</summary>
        Teleporting = 2,
    }

    public partial class ServerAvatarImmigateComponent : ServerEntityComponent
    {
        public AvatarImmigrationState ImmigrationState { get; private set; }
        public Guid ImmigrationId { get; private set; }
        public EntityMailBox ImmigrationTargetSpace { get; private set; }

        internal void ChangeToGhost(Guid migrationId, EntityMailBox targetSpace)
        {
            BeginMigration(AvatarImmigrationState.Ghost, migrationId, targetSpace);
        }

        internal void BeginTeleport(Guid migrationId, EntityMailBox targetSpace)
        {
            BeginMigration(AvatarImmigrationState.Teleporting, migrationId, targetSpace);
        }

        private void BeginMigration(AvatarImmigrationState state, Guid migrationId, EntityMailBox targetSpace)
        {
            if (ImmigrationState != AvatarImmigrationState.Real || migrationId == Guid.Empty || !targetSpace.IsValid)
            {
                throw new InvalidOperationException($"Invalid Avatar migration transition, current={ImmigrationState}, target={state}.");
            }
            ImmigrationId = migrationId;
            ImmigrationTargetSpace = targetSpace;
            ImmigrationState = state;
        }

        /// <summary>changeToReal on failure, or removal of TELEPORT_START on target MigrationOver.</summary>
        internal void ChangeToReal()
        {
            ImmigrationState = AvatarImmigrationState.Real;
            ImmigrationId = Guid.Empty;
            ImmigrationTargetSpace = default;
        }
    }
}
