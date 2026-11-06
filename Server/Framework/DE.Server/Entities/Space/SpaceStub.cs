using System;
using System.Collections.Generic;
using System.Linq;
using DE.Server.Entities;
using DE.Server.NativeBridge;
using DE.Share.Data;
using DE.Share.DataFramework;
using DE.Share.Rpc;

namespace DE.Server.Stubs
{
    public partial class SpaceStub : ServerStubEntity
    {
        private readonly Dictionary<Guid, int> _attempts = new();
        private readonly Dictionary<Guid, CachedAllocation> _allocations = new();
        private ulong _retryTimer;
        private bool _stopped;
        private bool _failed;
        public SpaceRegistry Registry { get; private set; }

        protected override void PreInitialize()
        {
            base.PreInitialize();
            Registry = new SpaceRegistry();
        }

        protected internal override void OnUnregistered()
        {
            Shutdown();
            base.OnUnregistered();
        }

        public override void InitStub()
        {
            var games = ManagedRuntimeState.RequireCurrent().GetGameServerIds();
            var rows = DataRuntime.GetTable<SpaceDataTable>().GetRows().OrderBy(row => row.Id.AsInt32()).ToArray();
            if (rows.Any(row => row.PersistentCount < 0 || row.MaxPlayers < 0))
            {
                throw new InvalidOperationException("Space PersistentCount and MaxPlayers must be nonnegative.");
            }
            int hostIndex = 0;
            foreach (var row in rows)
            {
                for (int index = 0; index < row.PersistentCount; index++)
                {
                    if (games.Count == 0)
                    {
                        throw new InvalidOperationException("No Game is available for persistent Spaces.");
                    }
                    var instance = Registry.Plan(row.Id.AsInt32(), games[hostIndex++ % games.Count], row.MaxPlayers);
                    _attempts.Add(instance.SpaceId, 0);
                }
            }
            if (Registry.IsReady)
            {
                OnReady();
                return;
            }
            _retryTimer = DETimer.AddTimer(5000, SendPendingCreates, true);
            SendPendingCreates();
        }

        private void SendPendingCreates()
        {
            if (_stopped || _failed)
            {
                return;
            }
            foreach (var instance in Registry.Instances.ToArray())
            {
                if (instance.IsReady)
                {
                    continue;
                }
                if (_attempts[instance.SpaceId] >= 3)
                {
                    _failed = true;
                    StopTimer();
                    DELogger.Error(nameof(SpaceStub), $"Persistent Space timed out, spaceNo={instance.SpaceNo}, spaceId={instance.SpaceId}.");
                    return;
                }
                _attempts[instance.SpaceId]++;
                var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
                RemoteEntityCaller.Create(instance.MailBox.BindingGame, runtime.SpaceType, instance.SpaceId,
                    new SpaceInitializer { SpaceNo = instance.SpaceNo });
            }
        }

        [ServerRpc]
        public void ReportSpace(EntityMailBox mailbox, int spaceNo, int avatarCount, ulong revision)
        {
            if (_stopped || _failed || !mailbox.IsValid || avatarCount < 0)
            {
                return;
            }
            var instance = Registry.Instances.FirstOrDefault(item => item.SpaceId == mailbox.EntityId);
            if (instance == null || instance.SpaceNo != spaceNo || instance.MailBox.BindingGame != mailbox.BindingGame)
            {
                return;
            }
            bool wasReady = instance.IsReady;
            if (!Registry.Confirm(mailbox, spaceNo))
            {
                return;
            }
            Registry.ReportLoad(mailbox.EntityId, mailbox.BindingGame, avatarCount, revision);
            if (!wasReady)
            {
                DELogger.Info(nameof(SpaceStub),
                    $"Persistent Space ready, spaceNo={spaceNo}, spaceId={mailbox.EntityId}, game={mailbox.BindingGame}.");
                if (Registry.IsReady)
                {
                    StopTimer();
                    OnReady();
                }
            }
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
            if (_stopped || _failed || !Registry.IsReady)
            {
                SendResolution(requestId, avatarMailbox, null, "Space service is not ready");
                return;
            }
            foreach (var key in _allocations.Where(pair => pair.Value.ExpiresAt <= DateTime.UtcNow).Select(pair => pair.Key).ToArray())
            {
                _allocations.Remove(key);
            }
            if (_allocations.TryGetValue(requestId, out var cached))
            {
                if (cached.AvatarMailbox.EntityId != avatarMailbox.EntityId || cached.AvatarMailbox.BindingGame != avatarMailbox.BindingGame
                    || cached.SpaceNo != spaceNo || cached.SpaceId != spaceId || cached.ById != byId)
                {
                    return;
                }
                SendResolution(requestId, avatarMailbox, cached.Instance, cached.Error);
                return;
            }
            if (_allocations.Count >= 16384)
            {
                SendResolution(requestId, avatarMailbox, null, "Space allocation request limit reached");
                return;
            }
            SpaceInstance selected = null;
            string error = string.Empty;
            try
            {
                selected = byId ? Registry.Find(spaceId) : Registry.Allocate(spaceNo, avatarMailbox.EntityId);
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
            _allocations.Add(requestId, new CachedAllocation
            {
                AvatarMailbox = avatarMailbox,
                SpaceNo = spaceNo,
                SpaceId = spaceId,
                ById = byId,
                Instance = selected,
                Error = error,
                ExpiresAt = DateTime.UtcNow.AddMinutes(2),
            });
            SendResolution(requestId, avatarMailbox, selected, error);
        }

        private void SendResolution(Guid requestId, EntityMailBox avatarMailbox, SpaceInstance instance, string error)
        {
            ServerMailboxCaller.Call(avatarMailbox, nameof(AvatarEntity.OnSpaceResolved), requestId,
                instance?.MailBox ?? default(EntityMailBox), instance?.SpaceNo ?? 0, error);
        }

        internal void Shutdown()
        {
            _stopped = true;
            StopTimer();
            _allocations.Clear();
        }

        private void StopTimer()
        {
            if (_retryTimer != 0)
            {
                DETimer.CancelTimer(_retryTimer);
                _retryTimer = 0;
            }
        }

        private sealed class CachedAllocation
        {
            public EntityMailBox AvatarMailbox;
            public Guid SpaceId;
            public int SpaceNo;
            public bool ById;
            public SpaceInstance Instance;
            public string Error;
            public DateTime ExpiresAt;
        }
    }
}
