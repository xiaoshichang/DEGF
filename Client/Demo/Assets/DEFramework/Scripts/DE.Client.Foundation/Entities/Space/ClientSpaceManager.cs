using System;

namespace DE.Client.Entities
{
    public sealed class SpaceLoadingContext
    {
        private readonly ClientSpaceManager _manager;

        internal SpaceLoadingContext(ClientSpaceManager manager, long sequence, ClientSpaceEntity space)
        {
            _manager = manager;
            Sequence = sequence;
            Space = space;
        }

        public long Sequence { get; }
        public ClientSpaceEntity Space { get; }
        public int SpaceNo => Space.SpaceNo;
        public bool IsCurrent => ReferenceEquals(_manager.Pending, this);
        internal bool HasStartedLoading { get; set; }

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
        private readonly ClientAvatarEntity _owner;
        private long _sequence;
        private Action<SpaceLoadingContext> _load;

        public ClientSpaceManager(ClientAvatarEntity owner)
        {
            _owner = owner;
        }

        public ClientSpaceEntity Current { get; private set; }
        public SpaceLoadingContext Pending { get; private set; }
        public Action<SpaceLoadingContext> Load
        {
            get => _load;
            set
            {
                _load = value;
                StartLoading();
            }
        }

        public event Action<SpaceLoadingContext> LoadingStarted;
        public event Action<ClientSpaceEntity> LoadingCompleted;
        public event Action<string> LoadingFailed;
        public event Action<string> EntryFailed;

        public void NotifyEntryFailed(string error)
        {
            // A failed new request must not cancel loading for the restored source Space.
            ClientSpaceEntity.InvokeSafely(EntryFailed, error);
        }

        public void BeginLoading(string spaceType, byte[] spaceData, byte[] ownerData, string[] entityTypes, byte[][] entityData)
        {
            // Apply the Owner snapshot before later Gate-ordered patches arrive during loading.
            EntitySerializer.ApplySnapshot(_owner, EntitySerializationProfile.Owner, ownerData);
            var staged = (ClientSpaceEntity)ClientEntityFactory.Create(spaceType, spaceData);
            for (int index = 0; index < entityTypes.Length; index++)
            {
                staged.Add(ClientEntityFactory.Create(entityTypes[index], entityData[index]));
            }
            var previous = Pending;
            Pending = new SpaceLoadingContext(this, ++_sequence, staged);
            previous?.Space.UnInitializeEntity();
            var context = Pending;
            ClientSpaceEntity.InvokeSafely(LoadingStarted, context);
            if (!context.IsCurrent)
            {
                return;
            }
            StartLoading();
        }

        private void StartLoading()
        {
            var context = Pending;
            if (context == null)
            {
                return;
            }
            if (context.HasStartedLoading)
            {
                return;
            }
            if (_load == null)
            {
                // Keep the staged Space and its AOI deltas until the client installs a loader.
                return;
            }
            context.HasStartedLoading = true;
            _load(context);
        }

        internal bool Complete(SpaceLoadingContext context)
        {
            if (!ReferenceEquals(Pending, context))
            {
                return false;
            }
            var previous = Current;
            Current = context.Space;
            Pending = null;
            previous?.UnInitializeEntity();
            ClientSpaceEntity.InvokeSafely(LoadingCompleted, Current);
            return true;
        }

        public void EntityAppeared(Guid spaceId, string entityType, byte[] entityData)
        {
            var space = FindContext(spaceId);
            if (space == null)
            {
                return;
            }
            space.Add(ClientEntityFactory.Create(entityType, entityData));
        }

        public void EntityDisappeared(Guid spaceId, Guid entityId)
        {
            FindContext(spaceId)?.Remove(entityId);
        }

        internal ClientEntity FindPropertyTarget(Guid entityId)
        {
            return FindPropertyTarget(Pending?.Space, entityId) ?? FindPropertyTarget(Current, entityId);
        }

        private static ClientEntity FindPropertyTarget(ClientSpaceEntity space, Guid entityId)
        {
            if (space == null)
            {
                return null;
            }
            if (space.Guid == entityId)
            {
                return space;
            }
            return space.Entities.TryGetValue(entityId, out var entity) ? entity : null;
        }

        private ClientSpaceEntity FindContext(Guid spaceId)
        {
            // Incoming deltas update the staged dictionary while resources load.
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
