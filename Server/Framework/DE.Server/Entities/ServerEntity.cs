using System;
using System.Collections.Generic;
using DE.Share.Entities;

namespace DE.Server.Entities
{
    public abstract partial class ServerEntity : Entity
    {
        protected ServerEntity()
        {
        }

        protected ServerEntity(object entityDocument) : base(entityDocument)
        {
        }

        public abstract bool IsMigratable();
    }
}
