using System;
using System.Collections.Generic;

namespace DE.Share.Entities
{
    public abstract partial class Entity
    {
        protected Entity()
        {
        }

        protected Entity(object entityDocument)
        {
        }

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.Persistent)]
        private Guid __Guid;
    }
}
