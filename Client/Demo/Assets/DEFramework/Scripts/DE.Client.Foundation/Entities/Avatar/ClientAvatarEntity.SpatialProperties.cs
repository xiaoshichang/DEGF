using System;
using DE.Share.Entities;
using DE.Share.Foundation.Mathematics;

namespace DE.Client.Entities
{
    public partial class ClientAvatarEntity
    {
        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Guid __SpaceId;

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private int __SpaceNo;

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Vector3 __Position;

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Quaternion __Rotation;
    }
}
