using System;
using System.Collections.Generic;
using System.Linq;
using DE.Server.NativeBridge;

namespace DE.Server.Entities
{
    public abstract partial class ServerEntity
    {
        /// <summary>是否记录脏属性并自动同步到客户端；派生实体按需要开启。</summary>
        public virtual bool AutoSyncProperty => false;

        private static readonly ulong GuidPropertyId = EntityNameHash.Compute("Guid");
        private EntityPropertyChangeJournal _propertyChanges;
        private bool _propertyWritesFrozen;

        public bool IsPropertyChangeTrackingEnabled => AutoSyncProperty && IsInitialized;

        private EntityPropertyChangeJournal DirtyProperties => _propertyChanges ?? (_propertyChanges = new EntityPropertyChangeJournal());

        public void ValidatePropertyWrite(ulong propertyId, ServerEntityComponent component = null)
        {
            EnsurePropertyWritesNotFrozen();
            if (component == null && IsInitialized && propertyId == GuidPropertyId)
            {
                throw new InvalidOperationException("The identity of an initialized entity cannot be changed.");
            }
            if (IsPropertyChangeTrackingEnabled && PropertySequence == ulong.MaxValue)
            {
                throw new InvalidOperationException("The entity property sequence is exhausted.");
            }
        }

        public void ValidatePropertyChangeCapacity(int valueLength, ServerEntityComponent component = null)
        {
            if (IsPropertyChangeTrackingEnabled)
            {
                if (component != null)
                {
                    GetComponentPropertyHash(component);
                }
                DirtyProperties.EnsureCapacity(valueLength, component == null ? 1 : 2);
            }
        }

        internal void FreezePropertyWrites()
        {
            _propertyWritesFrozen = true;
        }

        internal void UnfreezePropertyWrites()
        {
            _propertyWritesFrozen = false;
        }

        private void EnsurePropertyWritesNotFrozen()
        {
            if (IsInitialized && _propertyWritesFrozen)
            {
                throw new InvalidOperationException("Entity property writes are frozen for migration.");
            }
        }

        public void RecordPropertyChange(
            ulong propertyId, byte typeCode, EntitySerializationProfileMask profiles, byte[] value, ServerEntityComponent component = null)
        {
            if (!IsPropertyChangeTrackingEnabled)
            {
                return;
            }
            var path = component == null ? new[] { propertyId }
                : new[] { GetComponentPropertyHash(component), propertyId };
            DirtyProperties.Append(path, typeCode, profiles, value);
        }

        private ulong GetComponentPropertyHash(ServerEntityComponent component)
        {
            if (!((IEntityPropertyNode)this).TryGetComponentPropertyHash(component, out ulong propertyHash))
            {
                throw new InvalidOperationException("A tracked component must be held by a declared EntityComponent property.");
            }
            return propertyHash;
        }

        private void ResetAutoSyncProperties()
        {
            _propertyChanges = null;
            _propertyWritesFrozen = false;
        }

        internal IReadOnlyList<EntityPropertyChange> PendingPropertyChanges => _propertyChanges?.Changes ?? Array.Empty<EntityPropertyChange>();

        internal void ClearPropertyChanges()
        {
            _propertyChanges?.ClearPending();
        }

        internal ulong PropertySequence => _propertyChanges?.Sequence ?? 0;

        internal void RebasePropertySequence(ulong sequence)
        {
            if (AutoSyncProperty)
            {
                DirtyProperties.Rebase(sequence);
            }
        }

        internal static bool CanSynchronizeAvatar(ServerAvatarEntity avatar)
        {
            if (avatar == null || !avatar.IsInitialized || !avatar.Proxy.IsValid)
            {
                return false;
            }
            return avatar.ImmigateCmpt.ImmigrationState == AvatarImmigrationState.Idle
                || avatar.ImmigateCmpt.ImmigrationState == AvatarImmigrationState.Completed
                || avatar.ImmigateCmpt.ImmigrationState == AvatarImmigrationState.Failed;
        }

        internal bool SynchronizeProperties(GameServerRuntimeState runtime, bool ownerOnly = false)
        {
            if (!AutoSyncProperty)
            {
                return true;
            }
            var changes = PendingPropertyChanges;
            if (changes.Count == 0)
            {
                return true;
            }
            var targets = new Dictionary<ServerAvatarEntity, EntitySerializationProfile>();
            if (this is ServerAvatarEntity avatar)
            {
                if (runtime.AvatarClientSessionIds.ContainsKey(avatar.Guid))
                {
                    targets[avatar] = EntitySerializationProfile.Owner;
                }
                var agent = avatar.AOIAgentCmpt.Agent;
                if (!ownerOnly && agent != null)
                {
                    foreach (var observer in agent.Observers)
                    {
                        if (observer.Id != avatar.Guid && runtime.Avatars.TryGetValue(observer.Id, out var recipient)
                            && CanSynchronizeAvatar(recipient) && runtime.AvatarClientSessionIds.ContainsKey(recipient.Guid))
                        {
                            targets[recipient] = EntitySerializationProfile.Observer;
                        }
                    }
                }
            }
            else if (this is ServerSpaceEntity space)
            {
                foreach (var recipient in space.AvatarManageCmpt.Avatars.Values)
                {
                    if (CanSynchronizeAvatar(recipient) && runtime.AvatarClientSessionIds.ContainsKey(recipient.Guid))
                    {
                        targets[recipient] = EntitySerializationProfile.Observer;
                    }
                }
            }

            bool sent = true;
            foreach (var target in targets)
            {
                var mask = (EntitySerializationProfileMask)(1 << ((int)target.Value - 1));
                if (!changes.Any(change => (change.Profiles & mask) != 0))
                {
                    continue;
                }
                byte[] patch = EntitySerializer.SerializePatch(this, target.Value);
                if (!target.Key.CallClient("OnEntityPropertyPatch", Guid, (int)target.Value, patch))
                {
                    sent = false;
                }
            }
            if (sent)
            {
                ClearPropertyChanges();
            }
            return sent;
        }

        /// <summary>维护每个 Game 的自动同步定时器；运行时只负责启动和停止。</summary>
        internal sealed class AutoSyncPropertySynchronizer
        {
            private readonly GameServerRuntimeState _runtime;
            private ulong _timer;

            internal AutoSyncPropertySynchronizer(GameServerRuntimeState runtime)
            {
                _runtime = runtime;
            }

            internal void Start()
            {
                if (_timer == 0)
                {
                    _timer = DETimer.AddTimer(50, Flush, true);
                }
            }

            internal void Stop()
            {
                if (_timer != 0)
                {
                    DETimer.CancelTimer(_timer);
                    _timer = 0;
                }
            }

            private void Flush()
            {
                _runtime.RetryPendingSpaceEntries();
                // Timers run on the Game thread; snapshots and later patches share Gate FIFO order.
                foreach (var entity in _runtime.Entities.Values.ToArray())
                {
                    if (!entity.AutoSyncProperty || !entity.IsInitialized
                        || entity is ServerAvatarEntity avatar && !CanSynchronizeAvatar(avatar))
                    {
                        continue;
                    }
                    entity.SynchronizeProperties(_runtime);
                }
            }
        }

        /// <summary>实体统一变化日志，跨组件保序；同步时发送全部待发送变化，成功后清空，不合并重复赋值。</summary>
        private sealed class EntityPropertyChangeJournal
        {
            private const int ChangeOverheadBytes = 64;
            // All pending changes must fit one packet, excluding its 24-byte header and 4-byte record count.
            private const int MaximumPendingChangeBytes = EntitySerializationLimits.MaximumPacketBytes - 28;
            private const int MaximumPendingChanges = EntitySerializationLimits.MaximumEntries;
            private readonly List<EntityPropertyChange> _changes = new List<EntityPropertyChange>();
            private int _pendingBytes;

            internal ulong Sequence { get; private set; }
            internal IReadOnlyList<EntityPropertyChange> Changes => _changes;

            internal void EnsureCapacity(int valueLength, int pathLength)
            {
                if (valueLength < 0 || valueLength > EntitySerializationLimits.MaximumValueBytes)
                {
                    throw new ArgumentOutOfRangeException(nameof(valueLength), "An encoded property value must not exceed 1 MiB.");
                }
                if (pathLength <= 0 || pathLength > EntitySerializationLimits.MaximumDepth)
                {
                    throw new ArgumentOutOfRangeException(nameof(pathLength));
                }
                int size = GetChangeSize(valueLength, pathLength);
                if (_changes.Count >= MaximumPendingChanges
                    || size > MaximumPendingChangeBytes - _pendingBytes)
                {
                    throw new InvalidOperationException("The pending entity property journal is full; synchronize changes before writing more properties.");
                }
            }

            internal void Append(IReadOnlyList<ulong> path, byte typeCode, EntitySerializationProfileMask profiles, byte[] value)
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }
                if (path == null)
                {
                    throw new ArgumentNullException(nameof(path));
                }
                EnsureCapacity(value.Length, path.Count);
                ulong sequence = checked(Sequence + 1);
                var change = new EntityPropertyChange(sequence, path, typeCode, value, profiles);
                _changes.Add(change);
                _pendingBytes += GetChangeSize(value.Length, path.Count);
                Sequence = sequence;
            }

            internal void Rebase(ulong sequence)
            {
                if (_changes.Count != 0)
                {
                    throw new InvalidOperationException("Cannot rebase an entity with pending local property changes.");
                }
                Sequence = sequence;
            }

            internal void ClearPending()
            {
                _changes.Clear();
                _pendingBytes = 0;
            }

            internal void Clear()
            {
                ClearPending();
                Sequence = 0;
            }

            private static int GetChangeSize(int valueLength, int pathLength)
            {
                return checked(ChangeOverheadBytes + pathLength * sizeof(ulong) + valueLength);
            }
        }
    }
}
