using DE.Share.Entities;
using System;
using System.Collections.Generic;

namespace DE.Client.Entities
{
    public partial class ClientSpaceEntity : ClientEntity
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
                InvokeSafely(item => item.UnInitializeEntity(), entity);
                InvokeSafely(EntityRemoved, entity);
            }
        }

        /// <summary>移除并清理可见实体，解除空间事件订阅，然后清理组件。</summary>
        protected override void UnInitialize()
        {
            foreach (var id in new List<Guid>(_entities.Keys))
            {
                Remove(id);
            }
            InvokeSafely(item => item.OnRemovedFromSpace(), this);
            EntityAdded = null;
            EntityRemoved = null;
            base.UnInitialize();
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

}
