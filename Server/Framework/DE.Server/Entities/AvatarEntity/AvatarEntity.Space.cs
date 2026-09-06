using System;
using DE.Share.Entities;

namespace DE.Server.Entities
{
    public partial class AvatarEntity
    {
        [EntityProperty(EntityPropertyFlag.ServerOnly)]
        private Guid __CurrentSpaceId;

        public virtual void OnEnterSpace(SpaceEntity space)
        {
        }

        public virtual void OnLeaveSpace(SpaceEntity space)
        {
        }
    }
}
