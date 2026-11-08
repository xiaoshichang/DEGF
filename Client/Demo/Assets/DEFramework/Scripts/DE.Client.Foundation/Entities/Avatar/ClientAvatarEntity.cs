using System;
using Assets.Scripts.DE.Client.Framework;
using DE.Share.Entities;
using DE.Share.Foundation.Mathematics;
using DE.Share.Rpc;

namespace DE.Client.Entities
{
    public partial class ClientAvatarEntity : ClientEntity, ISpatialEntity
    {
        public ClientAvatarEntity()
        {
            Space = AddComponent(new ClientSpaceComponent());
        }

        public ClientAvatarEntity(object entityDocument) : base(entityDocument)
        {
            Space = AddComponent(new ClientSpaceComponent());
        }

        public ClientSpaceComponent Space { get; }
        public ClientSpaceManager Spaces { get; private set; }

        protected override void PreInitialize()
        {
            base.PreInitialize();
            SpaceId = Guid.Empty;
            SpaceNo = 0;
            Position = default;
            Rotation = Quaternion.Identity;
        }

        protected override void Initialize()
        {
            Spaces = new ClientSpaceManager(this);
            base.Initialize();
        }

        protected override void UnInitialize()
        {
            Spaces?.Reset();
            Spaces = null;
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

        public override void OnRemovedFromSpace()
        {
            Spaces?.Reset();
            base.OnRemovedFromSpace();
        }

        public bool CallServer(string methodName, params object[] args)
        {
            uint methodId = RpcMethodId.Compute(methodName, args);
            byte[] argsPayload = RpcBinaryWriter.SerializeArguments(args);
            return AuthSystem.Instance.SendAvatarServerRpc(methodId, argsPayload);
        }
    }
}
