using System;
using System.Collections.Generic;
using DE.Share.Entities;

namespace DE.Client.Entities
{
    public partial class ClientSpace : ClientEntity
    {
        [EntityProperty(EntityPropertyFlag.AllClients)]
        private int __SpaceNo;

        private readonly Dictionary<Guid, ClientEntity> _entities = new Dictionary<Guid, ClientEntity>();
        public IReadOnlyDictionary<Guid, ClientEntity> Entities => _entities;
        public event Action<ClientEntity> EntityAdded;
        public event Action<ClientEntity> EntityRemoved;

        internal void Add(ClientEntity entity)
        {
            Remove(entity.Guid);
            _entities.Add(entity.Guid, entity);
            InvokeSafely(EntityAdded, entity);
        }

        internal void Remove(Guid entityId)
        {
            if (_entities.TryGetValue(entityId, out var entity))
            {
                _entities.Remove(entityId);
                InvokeSafely(item => item.OnRemovedFromSpace(), entity);
                InvokeSafely(EntityRemoved, entity);
            }
        }

        internal void Release()
        {
            foreach (var id in new List<Guid>(_entities.Keys))
            {
                Remove(id);
            }
            InvokeSafely(item => item.OnRemovedFromSpace(), this);
        }

        internal static void InvokeSafely<T>(Action<T> callback, T argument)
        {
            if (callback == null)
            {
                return;
            }
            foreach (Action<T> handler in callback.GetInvocationList())
            {
                try
                {
                    handler(argument);
                }
                catch (Exception error)
                {
                    UnityEngine.Debug.LogException(error);
                }
            }
        }
    }

    public static class ClientEntityFactory
    {
        private static readonly Dictionary<string, Type> EntityTypes = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            // The framework's server SpaceEntity is represented by ClientSpace on the client.
            { "SpaceEntity", typeof(ClientSpace) },
            { nameof(AvatarEntity), typeof(AvatarEntity) },
        };

        public static void Register<T>() where T : ClientEntity, new()
        {
            Register(typeof(T));
        }

        public static void Register(Type entityType)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }
            if (!typeof(ClientEntity).IsAssignableFrom(entityType) || entityType.IsAbstract || entityType.ContainsGenericParameters
                || entityType.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new ArgumentException("Client entity type must be concrete and have a public parameterless constructor.", nameof(entityType));
            }
            EntityTypes[entityType.Name] = entityType;
        }

        public static ClientEntity Create(EntitySnapshot snapshot)
        {
            if (snapshot == null || !EntityTypes.TryGetValue(snapshot.TypeName, out var entityType))
            {
                throw new InvalidOperationException("Unregistered client entity type: " + snapshot?.TypeName);
            }
            var entity = (ClientEntity)Activator.CreateInstance(entityType);
            entity.InitializeEntity(snapshot.Data, EntitySerializeReason.Broadcase);
            if (entity.Guid != snapshot.EntityId)
            {
                throw new InvalidOperationException("Invalid client entity snapshot.");
            }
            return entity;
        }
    }
}
