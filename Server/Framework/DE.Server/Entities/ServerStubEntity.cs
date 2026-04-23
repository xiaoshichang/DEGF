using System;

namespace DE.Server.Entities
{
    public abstract class ServerStubEntity : ServerEntity
    {
        public ServerStubEntity()
        {
        }

        public ServerStubEntity(object entityDocument) : base(entityDocument)
        {
        }

        public override bool IsMigratable()
        {
            return false;
        }

        public abstract void OnStubReady();
    }
}
