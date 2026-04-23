using System;

namespace DE.Server.Entities
{
    public sealed class ServerStubEntity : ServerEntity
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
    }
}
