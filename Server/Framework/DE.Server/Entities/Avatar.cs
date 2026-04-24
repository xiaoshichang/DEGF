using System;
using DE.Share.Entities;

namespace DE.Server.Entities
{
    public partial class AvatarEntity : ServerEntity
    {
        public AvatarEntity()
        {
        }

        public AvatarEntity(object entityDocument) : base(entityDocument)
        {
        }

        public override bool IsMigratable()
        {
            return true;
        }
    }
}
