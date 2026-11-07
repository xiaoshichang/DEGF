using System;
using System.Collections.Generic;
using System.Linq;
using DE.Server.Entities;
using DE.Server.NativeBridge;
using DE.Share.Rpc;

namespace DE.Server.Stubs
{
    public enum SpaceStubState
    {
        Uninitialized,
        Initializing,
        WaitingForSpaceCreation,
        Ready,
        Failed,
        Shutdown,
    }

    public partial class SpaceStub : ServerStubEntity
    {
        private readonly Dictionary<Guid, SpaceEntityStubProxy> _spaceProxiesById = new();
        private readonly Dictionary<int, ISpaceAllocationStrategy> _allocationStrategiesBySpaceNo = new();
        private readonly Dictionary<Guid, CachedSpaceAllocationResult> _allocationResultsByRequestId = new();
        public SpaceStubState State { get; private set; } = SpaceStubState.Uninitialized;
        public IReadOnlyCollection<SpaceEntityStubProxy> Spaces => _spaceProxiesById.Values;
        public bool AreSpacesReady => _spaceProxiesById.Values.All(spaceProxy => spaceProxy.IsReady);
        public ISpaceCreationStrategy CreationStrategy { get; set; } = new RoundRobinSpaceCreationStrategy();
        public ISpaceAllocationStrategy DefaultAllocationStrategy { get; set; } = new RandomSpaceAllocationStrategy();

        public void SetAllocationStrategy(int spaceNo, ISpaceAllocationStrategy strategy)
        {
            _allocationStrategiesBySpaceNo[spaceNo] = strategy ?? throw new ArgumentNullException(nameof(strategy));
        }

        public SpaceEntityStubProxy FindSpace(Guid spaceId)
        {
            return _spaceProxiesById.TryGetValue(spaceId, out var spaceProxy) && spaceProxy.IsReady ? spaceProxy : null;
        }

        private SpaceEntityStubProxy AllocateSpace(int spaceNo, Guid avatarId)
        {
            var candidates = _spaceProxiesById.Values.Where(item => item.SpaceNo == spaceNo && item.IsReady && item.HasCapacity).ToList();
            var strategy = _allocationStrategiesBySpaceNo.TryGetValue(spaceNo, out var configured) ? configured : DefaultAllocationStrategy;
            var selected = strategy.Select(spaceNo, avatarId, candidates.AsReadOnly());
            return selected != null && candidates.Contains(selected) ? selected : null;
        }

        [ServerRpc]
        public void RequestSpaceByNo(Guid requestId, EntityMailBox avatarMailbox, int spaceNo)
        {
            Resolve(requestId, avatarMailbox, spaceNo, Guid.Empty, false);
        }

        [ServerRpc]
        public void RequestSpaceById(Guid requestId, EntityMailBox avatarMailbox, Guid spaceId)
        {
            Resolve(requestId, avatarMailbox, 0, spaceId, true);
        }

        private void Resolve(Guid requestId, EntityMailBox avatarMailbox, int spaceNo, Guid spaceId, bool byId)
        {
            if (requestId == Guid.Empty || !avatarMailbox.IsValid
                || !ManagedRuntimeState.RequireCurrent().GetGameServerIds().Contains(avatarMailbox.BindingGame))
            {
                return;
            }
            if (State == SpaceStubState.Shutdown)
            {
                DELogger.Error(nameof(SpaceStub), $"Space allocation rejected because the Stub is stopped, requestId={requestId}.");
                SendResolution(requestId, avatarMailbox, null, "Space service is not ready");
                return;
            }
            if (State != SpaceStubState.Ready)
            {
                DELogger.Error(nameof(SpaceStub), $"Space allocation rejected because the Stub is not ready, requestId={requestId}, state={State}.");
                SendResolution(requestId, avatarMailbox, null, "Space service is not ready");
                return;
            }
            foreach (var key in _allocationResultsByRequestId.Where(pair => pair.Value.ExpiresAt <= DateTime.UtcNow).Select(pair => pair.Key).ToArray())
            {
                _allocationResultsByRequestId.Remove(key);
            }
            if (_allocationResultsByRequestId.TryGetValue(requestId, out var cached))
            {
                if (cached.AvatarMailbox.EntityId != avatarMailbox.EntityId || cached.AvatarMailbox.BindingGame != avatarMailbox.BindingGame
                    || cached.SpaceNo != spaceNo || cached.SpaceId != spaceId || cached.ById != byId)
                {
                    return;
                }
                SendResolution(requestId, avatarMailbox, cached.SpaceProxy, cached.Error);
                return;
            }
            if (_allocationResultsByRequestId.Count >= 16384)
            {
                SendResolution(requestId, avatarMailbox, null, "Space allocation request limit reached");
                return;
            }
            SpaceEntityStubProxy selected = null;
            string error = string.Empty;
            try
            {
                selected = byId ? FindSpace(spaceId) : AllocateSpace(spaceNo, avatarMailbox.EntityId);
                if (selected == null)
                {
                    error = "Requested Space is unavailable";
                }
            }
            catch (Exception exception)
            {
                error = "Space allocation strategy failed";
                DELogger.Error(nameof(SpaceStub), exception.ToString());
            }
            _allocationResultsByRequestId.Add(requestId, new CachedSpaceAllocationResult
            {
                AvatarMailbox = avatarMailbox,
                SpaceNo = spaceNo,
                SpaceId = spaceId,
                ById = byId,
                SpaceProxy = selected,
                Error = error,
                ExpiresAt = DateTime.UtcNow.AddMinutes(2),
            });
            SendResolution(requestId, avatarMailbox, selected, error);
        }

        private void SendResolution(Guid requestId, EntityMailBox avatarMailbox, SpaceEntityStubProxy spaceProxy, string error)
        {
            ServerMailboxCaller.Call(avatarMailbox, nameof(AvatarEntity.OnSpaceResolved), requestId,
                spaceProxy?.MailBox ?? default(EntityMailBox), spaceProxy?.SpaceNo ?? 0, error);
        }

        internal void Shutdown()
        {
            State = SpaceStubState.Shutdown;
            _allocationResultsByRequestId.Clear();
            _spaceProxiesById.Clear();
        }

        private sealed class CachedSpaceAllocationResult
        {
            public EntityMailBox AvatarMailbox;
            public Guid SpaceId;
            public int SpaceNo;
            public bool ById;
            public SpaceEntityStubProxy SpaceProxy;
            public string Error;
            public DateTime ExpiresAt;
        }
    }
}
