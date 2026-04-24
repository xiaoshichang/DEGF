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
    
}
