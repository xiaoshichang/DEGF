using System;
using System.Collections.Generic;
using System.Linq;
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

        protected override void UnInitialize()
        {
            _entities.Clear();
            base.UnInitialize();
        }

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
}
