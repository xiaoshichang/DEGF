using System.Linq;
using DE.Server.AOI;
using DE.Server.NativeBridge;
using DE.Share.Entities;

namespace DE.Server.Entities
{
    public partial class AOISpaceComponent
    {
        public bool AddVisibleEntity(ServerEntity entity)
        {
            var spaceId = OwnerEntity.Guid;
            if (entity == null)
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Cannot publish a null entity, spaceId={spaceId}.");
                return false;
            }
            if (entity.Guid == spaceId)
            {
                DELogger.Error(nameof(AOISpaceComponent), $"A Space cannot publish itself as an AOI agent, spaceId={spaceId}.");
                return false;
            }
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (!runtime.Entities.TryGetValue(entity.Guid, out var registered))
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Cannot publish an unregistered entity, spaceId={spaceId}, entityId={entity.Guid}.");
                return false;
            }
            if (!ReferenceEquals(entity, registered))
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Another entity uses this ID, spaceId={spaceId}, entityId={entity.Guid}.");
                return false;
            }
            if (Contains(entity.Guid))
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Entity is already published in AOI, spaceId={spaceId}, entityId={entity.Guid}.");
                return false;
            }
            if (entity is AvatarEntity avatar)
            {
                if (!ReferenceEquals(avatar.CurrentSpace, OwnerEntity))
                {
                    DELogger.Error(nameof(AOISpaceComponent), $"Avatar belongs to a different Space, spaceId={spaceId}, avatarId={avatar.Guid}.");
                    return false;
                }
                if (avatar.ImmigrationState == AvatarImmigrationState.TargetPrepared)
                {
                    DELogger.Error(nameof(AOISpaceComponent), $"Cannot publish a prepared migration candidate, avatarId={avatar.Guid}.");
                    return false;
                }
                if (avatar.ImmigrationState == AvatarImmigrationState.TargetActivated)
                {
                    DELogger.Error(nameof(AOISpaceComponent), $"Cannot publish an uncommitted migration candidate, avatarId={avatar.Guid}.");
                    return false;
                }
            }
            var component = entity.Components.OfType<AOIAgentComponent>().SingleOrDefault();
            if (component == null)
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Entity has no AOIAgentComponent, spaceId={spaceId}, entityId={entity.Guid}.");
                return false;
            }
            if (entity is ISpatialEntity spatialEntity)
            {
                component.Agent.Position = spatialEntity.Position;
            }
            return Enter(component.Agent);
        }

        public bool RemoveVisibleEntity(ServerEntity entity)
        {
            if (entity == null)
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Cannot remove a null entity from AOI, spaceId={OwnerEntity.Guid}.");
                return false;
            }
            // Prepared migration candidates may leave without ever becoming visible.
            if (!Contains(entity.Guid))
            {
                return false;
            }
            return Leave(Space.Agents[entity.Guid]);
        }

        internal void CompleteAvatarEntry(AvatarEntity avatar)
        {
            var spaceId = OwnerEntity.Guid;
            if (avatar == null)
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Cannot complete entry for a null Avatar, spaceId={spaceId}.");
                return;
            }
            if (!ReferenceEquals(avatar.CurrentSpace, OwnerEntity))
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Avatar entry refers to a different Space, spaceId={spaceId}, avatarId={avatar.Guid}.");
                return;
            }
            var spaceEntity = (ServerSpaceEntity)OwnerEntity;
            if (!spaceEntity.Avatars.TryGetValue(avatar.Guid, out var member))
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Avatar entry has no membership record, spaceId={spaceId}, avatarId={avatar.Guid}.");
                return;
            }
            if (!ReferenceEquals(avatar, member))
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Another Avatar uses this membership ID, spaceId={spaceId}, avatarId={avatar.Guid}.");
                return;
            }
            // A migration candidate becomes visible only after Gate has committed its route.
            if (avatar.ImmigrationState == AvatarImmigrationState.TargetPrepared)
            {
                return;
            }
            if (!Contains(avatar.Guid))
            {
                if (!AddVisibleEntity(avatar))
                {
                    avatar.NotifySpaceEnterFailed("Failed to publish the entered Avatar in AOI");
                    return;
                }
            }
            SendInitialSnapshot(avatar);
        }

        private void OnAgentEventEnter(AOIAgent observer, AOIAgent target)
        {
            if (!ReferenceEquals(observer.Space, Space))
            {
                return;
            }
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (runtime.Entities[observer.Id] is not AvatarEntity avatar)
            {
                return;
            }
            if (!avatar.IsInitialized)
            {
                return;
            }
            var targetEntity = runtime.Entities[target.Id];
            if (!targetEntity.IsInitialized)
            {
                return;
            }
            var snapshot = SpaceSnapshotCodec.SerializeEntity(BuildSnapshot(targetEntity));
            avatar.CallClient("NotifySpaceEntityAppeared", OwnerEntity.Guid, snapshot);
        }

        private void OnAgentEventLeave(AOIAgent observer, AOIAgent target)
        {
            // A departing observer no longer needs deltas for the old Space.
            if (!ReferenceEquals(observer.Space, Space))
            {
                return;
            }
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (runtime.Entities[observer.Id] is not AvatarEntity avatar)
            {
                return;
            }
            if (!avatar.IsInitialized)
            {
                return;
            }
            avatar.CallClient("NotifySpaceEntityDisappeared", OwnerEntity.Guid, target.Id);
        }

        private void SendInitialSnapshot(AvatarEntity avatar)
        {
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            var visible = GetVisibleAgents(avatar.Guid).Select(agent => BuildSnapshot(runtime.Entities[agent.Id])).ToArray();
            var spaceEntity = (ServerSpaceEntity)OwnerEntity;
            var snapshot = new SpaceEnterSnapshot(spaceEntity.SpaceNo, BuildSnapshot(spaceEntity), visible,
                EntitySerializer.Serialize(avatar, EntitySerializeReason.OwnerSync));
            if (!avatar.CallClient("NotifySpaceEntered", SpaceSnapshotCodec.Serialize(snapshot)))
            {
                DELogger.Error(nameof(AOISpaceComponent), $"Failed to send Space entry snapshot, avatarId={avatar.Guid}, spaceId={OwnerEntity.Guid}.");
            }
        }

        private static EntitySnapshot BuildSnapshot(ServerEntity entity)
        {
            return new EntitySnapshot(entity.Guid, entity.GetType().Name, EntitySerializer.Serialize(entity, EntitySerializeReason.Broadcase));
        }
    }
}
