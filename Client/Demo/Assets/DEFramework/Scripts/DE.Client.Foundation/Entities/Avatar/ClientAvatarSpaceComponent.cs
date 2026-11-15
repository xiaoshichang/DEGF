using System;
using DE.Share.Entities;

namespace DE.Client.Entities
{
    public partial class ClientAvatarSpaceComponent : ClientEntityComponent
    {
        /// <summary>初始化空间标识与本地字段，默认属性随后可由快照覆盖。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            __SpaceId = System.Guid.Empty;
            __SpaceNo = 0;
        }

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Guid __SpaceId;

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private int __SpaceNo;

    }
}
