using DE.Share.Entities;
using System;
using DE.Share.Rpc;
using DE.Share.Foundation.Mathematics;

namespace DE.Client.Entities
{
    public partial class ClientAvatarTransformComponent : ClientEntityComponent
    {
        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Vector3 __Position;

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Quaternion __Rotation;

        /// <summary>初始化位置、旋转默认值及空间加载管理器，不依赖其他组件；默认属性随后可由快照覆盖。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            __Position = default;
            __Rotation = Quaternion.Identity;
            Spaces = new ClientSpaceManager((ClientAvatarEntity)OwnerEntity);
        }

        /// <summary>组件依赖清理完成后释放空间加载管理器引用。</summary>
        protected override void PostUnInitialize()
        {
            Spaces = null;
            base.PostUnInitialize();
        }

        public ClientSpaceManager Spaces { get; private set; }

        /// <summary>重置空间加载及可见实体状态并释放加载管理器。</summary>
        protected override void UnInitialize()
        {
            Spaces?.Reset();
            base.UnInitialize();
        }

        [ClientRpc]
        public virtual void OnEnterSpace(string spaceType, byte[] spaceData, byte[] ownerData, string[] entityTypes, byte[][] entityData)
        {
            Spaces.BeginLoading(spaceType, spaceData, ownerData, entityTypes, entityData);
        }

        [ClientRpc]
        public void NotifySpaceEntityAppeared(Guid spaceId, string entityType, byte[] entityData)
        {
            Spaces.EntityAppeared(spaceId, entityType, entityData);
        }

        [ClientRpc]
        public void NotifySpaceEntityDisappeared(Guid spaceId, Guid entityId)
        {
            Spaces.EntityDisappeared(spaceId, entityId);
        }

        [ClientRpc]
        public void NotifySpaceEnterFailed(string error)
        {
            Assets.Scripts.DE.Client.Core.DELogger.Error(error);
            Spaces.NotifyEntryFailed(error);
        }

        public void OnRemovedFromSpace()
        {
            Spaces?.Reset();
        }

    }
}
