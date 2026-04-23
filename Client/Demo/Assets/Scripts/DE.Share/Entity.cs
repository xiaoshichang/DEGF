using System;
using System.Collections.Generic;

namespace DE.Share.Entities
{
    public abstract class Entity
    {
        protected Entity()
        {
        }

        protected Entity(object entityDocument)
        {
        }

        public Guid EntityGuid { get; private set; }
    }

    public sealed class EntityDirectory<TEntity> where TEntity : Entity
    {
        private readonly Dictionary<Guid, TEntity> mEntities = new Dictionary<Guid, TEntity>();

        public int Count
        {
            get
            {
                return mEntities.Count;
            }
        }

        public IEnumerable<TEntity> Entities
        {
            get
            {
                return mEntities.Values;
            }
        }

        public bool Add(TEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (entity.EntityGuid == Guid.Empty)
            {
                throw new InvalidOperationException("Entity guid must be assigned before adding to the directory.");
            }

            if (mEntities.ContainsKey(entity.EntityGuid))
            {
                return false;
            }

            mEntities.Add(entity.EntityGuid, entity);
            return true;
        }

        public bool Remove(Guid entityGuid)
        {
            return mEntities.Remove(entityGuid);
        }

        public bool TryGet(Guid entityGuid, out TEntity entity)
        {
            return mEntities.TryGetValue(entityGuid, out entity);
        }

        public void Clear()
        {
            mEntities.Clear();
        }
    }
}
