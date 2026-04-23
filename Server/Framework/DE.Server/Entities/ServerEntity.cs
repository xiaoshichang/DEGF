using System;
using System.Collections.Generic;
using DE.Share.Entities;

namespace DE.Server.Entities
{
    public abstract class ServerEntity : Entity
    {
        protected ServerEntity()
        {
        }

        protected ServerEntity(object entityDocument) : base(entityDocument)
        {
        }

        public abstract bool IsMigratable();
    }

    public sealed class ServerEntityManager
    {
        private readonly EntityDirectory<ServerEntity> _Entities = new EntityDirectory<ServerEntity>();

        public int Count
        {
            get
            {
                return _Entities.Count;
            }
        }

        public IEnumerable<ServerEntity> Entities
        {
            get
            {
                return _Entities.Entities;
            }
        }

        public bool Add(ServerEntity entity)
        {
            return _Entities.Add(entity);
        }

        public bool Remove(Guid entityGuid)
        {
            return _Entities.Remove(entityGuid);
        }

        public bool TryGet(Guid entityGuid, out ServerEntity entity)
        {
            return _Entities.TryGet(entityGuid, out entity);
        }

        public void Clear()
        {
            _Entities.Clear();
        }
    }
}
