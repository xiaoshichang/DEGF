using System;
using System.Linq;
using DE.Server.NativeBridge;
using DE.Share.Data;
using DE.Share.DataFramework;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public partial class AvatarEntity
    {
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
                || (ImmigrationState != AvatarImmigrationState.Idle && ImmigrationState != AvatarImmigrationState.Completed
                    && ImmigrationState != AvatarImmigrationState.Failed))
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
        public void OnSpaceResolved(Guid requestId, EntityMailBox space, int spaceNo, string error)
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
                CurrentSpace.CompleteAvatarEntry(this);
                return;
            }
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            if (ImmigrationState == AvatarImmigrationState.Completed && space.BindingGame != MailBox.BindingGame)
            {
                // The explicit Avatar request starts a new lifecycle; Teleport itself retains its terminal-state guard.
                ResetCompletedImmigration();
            }
            if (!runtime.Teleport(this, space))
            {
                NotifySpaceEnterFailed("Failed to enter the requested Space");
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

        /// <summary>The client invokes this after constructing its Avatar, to enter or resynchronize the current Space.</summary>
        [ServerRpc]
        public void OnSpaceClientReady()
        {
            if (_spaceRequest != null
                || (ImmigrationState != AvatarImmigrationState.Idle && ImmigrationState != AvatarImmigrationState.Completed
                    && ImmigrationState != AvatarImmigrationState.Failed))
            {
                return;
            }
            if (CurrentSpace != null)
            {
                CurrentSpace.CompleteAvatarEntry(this);
                return;
            }
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
            DELogger.Error(nameof(AvatarEntity), $"Space entry failed, avatarId={Guid}, error={error}.");
            CallClient("NotifySpaceEnterFailed", error ?? "Space entry failed");
        }

        private sealed class SpaceRequest
        {
            public Guid RequestId;
            public Guid SpaceId;
            public int SpaceNo;
        }
    }
}
