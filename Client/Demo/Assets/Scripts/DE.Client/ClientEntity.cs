using System;
using System.Collections.Generic;
using DE.Share.Entities;

namespace DE.Client.Entities
{

    public abstract class ClientEntity : Entity
    {
        protected ClientEntity()
        {
        }

        protected ClientEntity(object entityDocument) : base(entityDocument)
        {
        }
    }

    [Serializable]
    public sealed class AvatarEntity : ClientEntity
    {
        public AvatarEntity()
        {
        }

        public AvatarEntity(object entityDocument) : base(entityDocument)
        {
        }
    }

    public sealed class ClientEntityManager
    {
        private readonly EntityDirectory<ClientEntity> mEntities = new EntityDirectory<ClientEntity>();

        public int Count
        {
            get
            {
                return mEntities.Count;
            }
        }

        public IEnumerable<ClientEntity> Entities
        {
            get
            {
                return mEntities.Entities;
            }
        }

        public bool Add(ClientEntity entity)
        {
            return mEntities.Add(entity);
        }

        public bool Remove(Guid entityGuid)
        {
            return mEntities.Remove(entityGuid);
        }

        public bool TryGet(Guid entityGuid, out ClientEntity entity)
        {
            return mEntities.TryGet(entityGuid, out entity);
        }

        public void Clear()
        {
            mEntities.Clear();
        }
    }
}
