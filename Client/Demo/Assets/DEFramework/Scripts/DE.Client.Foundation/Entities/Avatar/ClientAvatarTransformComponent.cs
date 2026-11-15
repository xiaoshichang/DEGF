using System;
using DE.Share.Rpc;
using DE.Share.Entities;
using DE.Share.Foundation.Mathematics;

namespace DE.Client.Entities
{
    public partial class ClientAvatarTransformComponent : ClientEntityComponent, IEntityTransform
    {
        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Vector3 __Position;

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Quaternion __Rotation;

        public EntityTransform Transform { get; private set; }

        /// <summary>初始化变换默认属性和本地字段，不依赖其他组件；默认属性随后可由快照覆盖。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            __Position = default;
            __Rotation = Quaternion.Identity;
            Transform = new EntityTransform(this);
            Spaces = new ClientSpaceManager((ClientAvatarEntity)OwnerEntity);
        }

        /// <summary>组件依赖清理完成后释放变换字段引用。</summary>
        protected override void PostUnInitialize()
        {
            Transform = null;
            Spaces = null;
            base.PostUnInitialize();
        }

        public void SetTransform(Vector3 position, Quaternion rotation)
        {
            Transform.SetTransform(position, rotation);
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
