using System;
using DE.Share.Entities;
using DE.Share.Foundation.Mathematics;

namespace DE.Server.Entities
{
    public partial class AvatarEntity
    {
        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Guid __SpaceId;

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private int __SpaceNo;

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Vector3 __Position;

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Quaternion __Rotation;

        /// <summary>The Space currently occupied by this Avatar; null before entry and after leaving.</summary>
        public SpaceEntity CurrentSpace { get; internal set; }

        public virtual void OnEnterSpace(SpaceEntity space)
        {
        }

        public virtual void OnLeaveSpace(SpaceEntity space)
        {
        }
    }
}
