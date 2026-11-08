using System;
using DE.Share.Entities;

namespace DE.Client.Entities
{
    public sealed class SpaceLoadingContext
    {
        private readonly ClientSpaceManager _manager;

        internal SpaceLoadingContext(ClientSpaceManager manager, long sequence, SpaceEnterSnapshot snapshot, ClientSpaceEntity space)
        {
            _manager = manager;
            Sequence = sequence;
            Snapshot = snapshot;
            Space = space;
        }

        public long Sequence { get; }
        public SpaceEnterSnapshot Snapshot { get; }
        public ClientSpaceEntity Space { get; }
        public int SpaceNo => Snapshot.SpaceNo;
        public bool IsCurrent => ReferenceEquals(_manager.Pending, this);

        public bool Complete()
        {
            return _manager.Complete(this);
        }

        public void Fail(string error)
        {
            if (IsCurrent)
            {
                _manager.Fail(error);
            }
        }
    }

    public sealed class ClientSpaceManager
    {
        private readonly AvatarEntity _owner;
        private long _sequence;

        public ClientSpaceManager(AvatarEntity owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public ClientSpaceEntity Current { get; private set; }
        public SpaceLoadingContext Pending { get; private set; }
        public Action<SpaceLoadingContext> Load { get; set; }
        public event Action<SpaceLoadingContext> LoadingStarted;
        public event Action<ClientSpaceEntity> LoadingCompleted;
        public event Action<string> LoadingFailed;
        public event Action<string> EntryFailed;

        public void NotifyEntryFailed(string error)
        {
            // An unsuccessful new request must not cancel resource loading for the restored source Space.
            ClientSpaceEntity.InvokeSafely(EntryFailed, error);
        }

        public void BeginLoading(SpaceEnterSnapshot snapshot)
        {
            ClientSpaceEntity staged = null;
            try
            {
                var spaceEntity = ClientEntityFactory.Create(snapshot.Space);
                staged = spaceEntity as ClientSpaceEntity;
                if (staged == null)
                {
                    spaceEntity.UnInitializeEntity();
                    throw new InvalidOperationException("Space entity type does not match the entry snapshot.");
                }
                if (staged.SpaceNo != snapshot.SpaceNo)
                {
                    throw new InvalidOperationException("Space number does not match the entry snapshot.");
                }
                foreach (var item in snapshot.Entities)
                {
                    if (item.EntityId == _owner.Guid || staged.Entities.ContainsKey(item.EntityId))
                    {
                        throw new InvalidOperationException("Space snapshot contains its observer or duplicate entities.");
                    }
                    staged.Add(ClientEntityFactory.Create(item));
                }
                // Validate owner data on a temporary entity before touching the active Avatar.
                var ownerCopy = (AvatarEntity)Activator.CreateInstance(_owner.GetType());
                try
                {
                    ownerCopy.InitializeEntity(snapshot.OwnerData, EntitySerializeReason.OwnerSync);
                    if (ownerCopy.Guid != _owner.Guid || ownerCopy.SpaceId != staged.Guid || ownerCopy.SpaceNo != staged.SpaceNo)
                    {
                        throw new InvalidOperationException("Space snapshot contains invalid owner data.");
                    }
                }
                finally
                {
                    ownerCopy.UnInitializeEntity();
                }
                var previous = Pending;
                Pending = new SpaceLoadingContext(this, ++_sequence, snapshot, staged);
                previous?.Space.UnInitializeEntity();
                var context = Pending;
                ClientSpaceEntity.InvokeSafely(LoadingStarted, context);
                if (!context.IsCurrent)
                {
                    return;
                }
                if (Load == null)
                {
                    context.Fail("No Space resource loader is registered.");
                    return;
                }
                Load(context);
            }
            catch (Exception error)
            {
                if (Pending?.Space == staged)
                {
                    Fail(error.Message);
                }
                else if (Current != staged)
                {
                    staged?.UnInitializeEntity();
                    ClientSpaceEntity.InvokeSafely(LoadingFailed, error.Message);
                }
                else
                {
                    UnityEngine.Debug.LogException(error);
                }
            }
        }

        internal bool Complete(SpaceLoadingContext context)
        {
            if (!ReferenceEquals(Pending, context))
            {
                return false;
            }
            try
            {
                EntitySerializer.Deserialize(_owner, EntitySerializeReason.OwnerSync, context.Snapshot.OwnerData);
            }
            catch (Exception error)
            {
                context.Fail(error.Message);
                return false;
            }
            var previous = Current;
            Current = context.Space;
            Pending = null;
            previous?.UnInitializeEntity();
            ClientSpaceEntity.InvokeSafely(LoadingCompleted, Current);
            return true;
        }

        public void EntityAppeared(Guid spaceId, EntitySnapshot snapshot)
        {
            var space = FindContext(spaceId);
            if (space == null || snapshot.EntityId == _owner.Guid)
            {
                return;
            }
            space.Add(ClientEntityFactory.Create(snapshot));
        }

        public void EntityDisappeared(Guid spaceId, Guid entityId)
        {
            FindContext(spaceId)?.Remove(entityId);
        }

        private ClientSpaceEntity FindContext(Guid spaceId)
        {
            // Incoming deltas are applied in order to the staged dictionary while resources load.
            if (Pending?.Space.Guid == spaceId)
            {
                return Pending.Space;
            }
            return Current?.Guid == spaceId ? Current : null;
        }

        public void Fail(string error)
        {
            var pending = Pending;
            Pending = null;
            pending?.Space.UnInitializeEntity();
            ClientSpaceEntity.InvokeSafely(LoadingFailed, error);
        }

        public void Reset()
        {
            var pending = Pending;
            var current = Current;
            Pending = null;
            Current = null;
            pending?.Space.UnInitializeEntity();
            current?.UnInitializeEntity();
        }
    }
}
