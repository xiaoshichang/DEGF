using System;
using System.Collections.Generic;
using System.Linq;
using DE.Server.NativeBridge;
using DE.Share.Entities;

namespace DE.Server.Entities
{
    public interface ISpaceAoiStrategy
    {
        void Enter(ServerEntity entity);
        void Leave(ServerEntity entity);
        IReadOnlyList<ServerEntity> GetVisibleEntities(ServerEntity observer);
    }

    public class SpaceAoiComponent : EntityComponent, ISpaceAoiStrategy
    {
        private readonly Dictionary<Guid, ServerEntity> _entities = new();

        public virtual void Enter(ServerEntity entity)
        {
            _entities.Add(entity.Guid, entity);
        }

        public virtual void Leave(ServerEntity entity)
        {
            _entities.Remove(entity.Guid);
        }

        public virtual IReadOnlyList<ServerEntity> GetVisibleEntities(ServerEntity observer)
        {
            return _entities.Values.Where(entity => entity.Guid != observer.Guid).ToArray();
        }
    }

    public partial class ServerSpaceEntity
    {
        private readonly Dictionary<Guid, ServerEntity> _publishedEntities = new();
        public SpaceAoiComponent Aoi { get; }

        public bool AddVisibleEntity(ServerEntity entity)
        {
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (entity == null || entity.Guid == Guid || !runtime.Entities.TryGetValue(entity.Guid, out var registered)
                || !ReferenceEquals(entity, registered) || _publishedEntities.ContainsKey(entity.Guid))
            {
                return false;
            }
            if (entity is AvatarEntity avatar && (!ReferenceEquals(avatar.CurrentSpace, this)
                || avatar.ImmigrationState == AvatarImmigrationState.TargetPrepared
                || avatar.ImmigrationState == AvatarImmigrationState.TargetActivated))
            {
                return false;
            }
            byte[] bytes = SpaceSnapshotCodec.SerializeEntity(BuildSnapshot(entity));
            Aoi.Enter(entity);
            _publishedEntities.Add(entity.Guid, entity);
            foreach (var observer in _publishedEntities.Values.OfType<AvatarEntity>().Where(item => item.Guid != entity.Guid).ToArray())
            {
                if (Aoi.GetVisibleEntities(observer).Any(item => item.Guid == entity.Guid))
                {
                    observer.CallClient("NotifySpaceEntityAppeared", Guid, bytes);
                }
            }
            return true;
        }

        public bool RemoveVisibleEntity(ServerEntity entity)
        {
            if (entity == null || !_publishedEntities.TryGetValue(entity.Guid, out var published) || !ReferenceEquals(entity, published))
            {
                return false;
            }
            var observers = _publishedEntities.Values.OfType<AvatarEntity>().Where(observer => observer.Guid != entity.Guid
                && Aoi.GetVisibleEntities(observer).Any(item => item.Guid == entity.Guid)).ToArray();
            Aoi.Leave(entity);
            _publishedEntities.Remove(entity.Guid);
            foreach (var observer in observers)
            {
                observer.CallClient("NotifySpaceEntityDisappeared", Guid, entity.Guid);
            }
            return true;
        }

        internal void CompleteAvatarEntry(AvatarEntity avatar)
        {
            if (avatar == null || !ReferenceEquals(avatar.CurrentSpace, this) || !_avatars.TryGetValue(avatar.Guid, out var member)
                || !ReferenceEquals(avatar, member))
            {
                return;
            }
            try
            {
                if (!_publishedEntities.ContainsKey(avatar.Guid))
                {
                    AddVisibleEntity(avatar);
                }
                SendInitialSnapshot(avatar);
            }
            catch (Exception error)
            {
                // Entry is already committed. A notification error must not roll back the route or membership.
                DELogger.Error(nameof(ServerSpaceEntity), $"Space entry notification failed, avatarId={avatar.Guid}, spaceId={Guid}: {error}");
                avatar.NotifySpaceEnterFailed("Failed to synchronize the entered Space");
            }
        }

        internal void SendInitialSnapshot(AvatarEntity avatar)
        {
            var visible = Aoi.GetVisibleEntities(avatar).Where(entity => entity.Guid != avatar.Guid).Select(BuildSnapshot).ToArray();
            var snapshot = new SpaceEnterSnapshot(SpaceNo, BuildSnapshot(this), visible,
                EntitySerializer.Serialize(avatar, EntitySerializeReason.OwnerSync));
            if (!avatar.CallClient("NotifySpaceEntered", SpaceSnapshotCodec.Serialize(snapshot)))
            {
                DELogger.Error(nameof(ServerSpaceEntity), $"Failed to send Space entry snapshot, avatarId={avatar.Guid}, spaceId={Guid}.");
            }
        }

        private static EntitySnapshot BuildSnapshot(ServerEntity entity)
        {
            return new EntitySnapshot(entity.Guid, entity.GetType().Name, EntitySerializer.Serialize(entity, EntitySerializeReason.Broadcase));
        }
    }
}
