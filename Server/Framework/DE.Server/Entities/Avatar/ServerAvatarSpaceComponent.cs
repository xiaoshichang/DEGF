using System;
using System.Linq;
using DE.Server.NativeBridge;
using DE.Share.Data;
using DE.Share.DataFramework;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public partial class ServerAvatarSpaceComponent : ServerEntityComponent
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

        public void SetSpace(Guid spaceId, int spaceNo)
        {
            SpaceId = spaceId;
            SpaceNo = spaceNo;
        }

        private ServerAvatarEntity Avatar => (ServerAvatarEntity)OwnerEntity;
        private System.Guid Guid => Avatar.Guid;
        private EntityProxy Proxy => Avatar.Proxy;
        private EntityMailBox MailBox => Avatar.MailBox;

        /// <summary>停止未完成的空间请求，保留当前空间关系供后续依赖清理。</summary>
        protected override void PreUnInitialize()
        {
            CancelSpaceRequest();
            base.PreUnInitialize();
        }

        /// <summary>解除 Avatar 与当前空间的依赖关系。</summary>
        protected override void UnInitialize()
        {
            CurrentSpace = null;
            base.UnInitialize();
        }

        /// <summary>The Space currently occupied by this Avatar; null before entry and after leaving.</summary>
        public ServerSpaceEntity CurrentSpace { get; internal set; }

        public virtual void OnBeforeEnterSpace(ServerSpaceEntity space)
        {
        }

        public virtual void OnAfterEnterSpace(ServerSpaceEntity space)
        {
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            var visible = space.AOISpace.GetVisibleAgents(Guid).Select(agent => runtime.Entities[agent.Id]).ToArray();
            var entityTypes = visible.Select(entity => entity.GetType().Name).ToArray();
            var entityData = visible.Select(entity => EntitySerializer.Serialize(entity, EntitySerializeReason.Broadcase)).ToArray();
            if (!Avatar.CallClient("OnEnterSpace", space.GetType().Name,
                EntitySerializer.Serialize(space, EntitySerializeReason.Broadcase),
                EntitySerializer.Serialize(Avatar, EntitySerializeReason.OwnerSync), entityTypes, entityData))
            {
                DELogger.Error(nameof(ServerAvatarEntity), $"Failed to notify Space entry, avatarId={Guid}, spaceId={space.Guid}.");
            }
        }

        public virtual void OnBeforeLeaveSpace(ServerSpaceEntity space)
        {
        }

        public virtual void OnAfterLeaveSpace(ServerSpaceEntity space)
        {
        }

        private SpaceRequest _spaceRequest;
        private ulong _spaceRequestTimer;
        private int _spaceRequestAttempts;

        public bool RequestEnterSpaceByNo(int spaceNo)
        {
            return spaceNo > 0 && BeginSpaceRequest(spaceNo, Guid.Empty);
        }

        public bool RequestEnterSpaceById(Guid spaceId)
        {
            return spaceId != Guid.Empty && BeginSpaceRequest(0, spaceId);
        }

        private bool BeginSpaceRequest(int spaceNo, Guid spaceId)
        {
            if (_spaceRequest != null || !Proxy.IsValid
                || (Avatar.ImmigateCmpt.ImmigrationState != AvatarImmigrationState.Idle
                    && Avatar.ImmigateCmpt.ImmigrationState != AvatarImmigrationState.Completed
                    && Avatar.ImmigateCmpt.ImmigrationState != AvatarImmigrationState.Failed))
            {
                return false;
            }
            _spaceRequest = new SpaceRequest
            {
                RequestId = Guid.NewGuid(),
                SpaceNo = spaceNo,
                SpaceId = spaceId,
            };
            _spaceRequestAttempts = 0;
            _spaceRequestTimer = DETimer.AddTimer(5000, SendSpaceRequest, true);
            SendSpaceRequest();
            return true;
        }

        private void SendSpaceRequest()
        {
            if (_spaceRequest == null)
            {
                return;
            }
            if (_spaceRequestAttempts++ >= 3)
            {
                CancelSpaceRequest();
                NotifySpaceEnterFailed("Space allocation timed out");
                return;
            }
            if (_spaceRequest.SpaceId != Guid.Empty)
            {
                ServerStubCaller.Call("SpaceStub", "RequestSpaceById", _spaceRequest.RequestId, MailBox, _spaceRequest.SpaceId);
            }
            else
            {
                ServerStubCaller.Call("SpaceStub", "RequestSpaceByNo", _spaceRequest.RequestId, MailBox, _spaceRequest.SpaceNo);
            }
        }

        [ServerRpc]
        public void OnRequestedSpaceReady(Guid requestId, EntityMailBox space, int spaceNo, string error)
        {
            var request = _spaceRequest;
            if (request == null || request.RequestId != requestId)
            {
                return;
            }
            CancelSpaceRequest();
            if (!string.IsNullOrEmpty(error))
            {
                NotifySpaceEnterFailed(error);
                return;
            }
            if (!space.IsValid || spaceNo <= 0 || !ManagedRuntimeState.RequireCurrent().GetGameServerIds().Contains(space.BindingGame)
                || (request.SpaceId != Guid.Empty && space.EntityId != request.SpaceId)
                || (request.SpaceId == Guid.Empty && spaceNo != request.SpaceNo))
            {
                NotifySpaceEnterFailed("Invalid Space allocation response");
                return;
            }
            if (CurrentSpace != null && CurrentSpace.Guid == space.EntityId)
            {
                NotifySpaceEnterFailed($"Space allocation returned the current Space, spaceId={space.EntityId}");
                return;
            }
            if (Avatar.ImmigateCmpt.ImmigrationState == AvatarImmigrationState.Completed && space.BindingGame != MailBox.BindingGame)
            {
                NotifySpaceEnterFailed($"Cannot start another cross-Game migration after completion, "
                    + $"targetSpaceId={space.EntityId}, targetGame={space.BindingGame}");
                return;
            }
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            var teleportError = runtime.Teleport(Avatar, space);
            if (teleportError != TeleportErrorCode.Success)
            {
                NotifySpaceEnterFailed($"Failed to enter the requested Space, errorCode={teleportError}, "
                    + $"targetSpaceId={space.EntityId}, targetGame={space.BindingGame}");
            }
        }

        internal void CancelSpaceRequest()
        {
            _spaceRequest = null;
            if (_spaceRequestTimer != 0)
            {
                DETimer.CancelTimer(_spaceRequestTimer);
                _spaceRequestTimer = 0;
            }
        }

        internal void EnterLoginSpace()
        {
            bool accepted;
            if (SpaceId != Guid.Empty)
            {
                accepted = RequestEnterSpaceById(SpaceId);
            }
            else
            {
                int spaceNo = SpaceNo;
                if (spaceNo <= 0)
                {
                    spaceNo = DataRuntime.GetTable<SpaceDataTable>().GetRows().Where(row => row.PersistentCount > 0)
                        .OrderBy(row => row.Id.AsInt32()).FirstOrDefault()?.Id.AsInt32() ?? 0;
                }
                accepted = RequestEnterSpaceByNo(spaceNo);
            }
            if (!accepted)
            {
                NotifySpaceEnterFailed("No default Space is configured or entry is already in progress");
            }
        }

        internal void NotifySpaceEnterFailed(string error)
        {
            DELogger.Error(nameof(ServerAvatarEntity), $"Space entry failed, avatarId={Guid}, error={error}.");
            Avatar.CallClient("NotifySpaceEnterFailed", error ?? "Space entry failed");
        }

        private sealed class SpaceRequest
        {
            public Guid RequestId;
            public Guid SpaceId;
            public int SpaceNo;
        }
    }
}
