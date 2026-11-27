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
        private EntityDirtyPropertySet _dirtyProperties;
        private bool _propertyWritesFrozen;

        public bool IsPropertyChangeTrackingEnabled => AutoSyncProperty && IsInitialized;

        private EntityDirtyPropertySet DirtyProperties => _dirtyProperties ?? (_dirtyProperties = new EntityDirtyPropertySet());

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

        public void ValidateDirtyPropertyCapacity(ulong propertyId, int valueLength, ServerEntityComponent component = null)
        {
            if (IsPropertyChangeTrackingEnabled)
            {
                DirtyProperties.EnsureCapacity(GetDirtyPropertyKey(propertyId, component), valueLength);
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

        public void MarkPropertyDirty(
            ulong propertyId, EntitySerializationProfileMask profiles, int valueLength, ServerEntityComponent component = null)
        {
            if (IsPropertyChangeTrackingEnabled)
            {
                DirtyProperties.Mark(GetDirtyPropertyKey(propertyId, component), component, profiles, valueLength);
            }
        }

        private (ulong? ComponentHash, ulong PropertyId) GetDirtyPropertyKey(ulong propertyId, ServerEntityComponent component)
        {
            return (component == null ? (ulong?)null : GetComponentPropertyHash(component), propertyId);
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
            _dirtyProperties = null;
            _propertyWritesFrozen = false;
        }

        internal IReadOnlyCollection<DirtyProperty> PendingDirtyProperties => _dirtyProperties?.Properties ?? Array.Empty<DirtyProperty>();

        internal void ClearDirtyProperties()
        {
            _dirtyProperties?.ClearPending();
        }

        internal ulong PropertySequence => _dirtyProperties?.Sequence ?? 0;

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
            // KBE's target is Real even while TELEPORT_START blocks another teleport.
            return avatar.ImmigateCmpt.ImmigrationState != AvatarImmigrationState.Ghost;
        }

        internal void SynchronizeProperties(GameServerRuntimeState runtime, bool ownerOnly = false)
        {
            if (!AutoSyncProperty)
            {
                return;
            }
            var changes = PendingDirtyProperties;
            if (changes.Count == 0)
            {
                return;
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

            foreach (var target in targets)
            {
                var mask = (EntitySerializationProfileMask)(1 << ((int)target.Value - 1));
                if (!changes.Any(change => (change.Profiles & mask) != 0))
                {
                    continue;
                }
                byte[] patch = EntitySerializer.SerializePatch(this, target.Value);
                target.Key.CallClient("OnEntityPropertyPatch", Guid, (int)target.Value, patch);
            }
            ClearDirtyProperties();
        }

        /// <summary>一个脏属性的标记与容量信息；不保存属性值或修改历史，发送时由 SG 读取字段。</summary>
        internal readonly struct DirtyProperty
        {
            internal DirtyProperty(
                ulong propertyId, ulong? componentHash, ServerEntityComponent component,
                EntitySerializationProfileMask profiles, int valueLength, ulong sequence)
            {
                PropertyId = propertyId;
                ComponentHash = componentHash;
                Component = component;
                Profiles = profiles;
                ValueLength = valueLength;
                Sequence = sequence;
            }

            internal ulong PropertyId { get; }
            internal ulong? ComponentHash { get; }
            internal ServerEntityComponent Component { get; }
            internal EntitySerializationProfileMask Profiles { get; }
            internal int ValueLength { get; }
            internal ulong Sequence { get; }
        }

        /// <summary>按完整属性路径去重；每个属性只保留一个脏标记，重复修改仅更新容量和诊断序号。</summary>
        private sealed class EntityDirtyPropertySet
        {
            private const int PropertyOverheadBytes = 64;
            private const int MaximumPendingBytes = EntitySerializationLimits.MaximumPacketBytes - 28;
            private const int MaximumPendingProperties = EntitySerializationLimits.MaximumEntries;
            private readonly Dictionary<(ulong? ComponentHash, ulong PropertyId), DirtyProperty> _properties =
                new Dictionary<(ulong? ComponentHash, ulong PropertyId), DirtyProperty>();
            private int _pendingBytes;

            internal ulong Sequence { get; private set; }
            internal IReadOnlyCollection<DirtyProperty> Properties => _properties.Values;

            internal void EnsureCapacity((ulong? ComponentHash, ulong PropertyId) key, int valueLength)
            {
                if (valueLength < 0 || valueLength > EntitySerializationLimits.MaximumValueBytes)
                {
                    throw new ArgumentOutOfRangeException(nameof(valueLength), "An encoded property value must not exceed 1 MiB.");
                }
                bool existing = _properties.TryGetValue(key, out var previous);
                int oldSize = existing ? GetPropertySize(previous.ValueLength, key.ComponentHash.HasValue) : 0;
                int newSize = GetPropertySize(valueLength, key.ComponentHash.HasValue);
                if ((!existing && _properties.Count >= MaximumPendingProperties)
                    || newSize > MaximumPendingBytes - (_pendingBytes - oldSize))
                {
                    throw new InvalidOperationException("The dirty property set is full; synchronize properties before writing more values.");
                }
            }

            internal void Mark(
                (ulong? ComponentHash, ulong PropertyId) key, ServerEntityComponent component,
                EntitySerializationProfileMask profiles, int valueLength)
            {
                EnsureCapacity(key, valueLength);
                ulong sequence = checked(Sequence + 1);
                if (_properties.TryGetValue(key, out var previous))
                {
                    _pendingBytes -= GetPropertySize(previous.ValueLength, key.ComponentHash.HasValue);
                }
                _properties[key] = new DirtyProperty(key.PropertyId, key.ComponentHash, component, profiles, valueLength, sequence);
                _pendingBytes += GetPropertySize(valueLength, key.ComponentHash.HasValue);
                Sequence = sequence;
            }

            internal void Rebase(ulong sequence)
            {
                if (_properties.Count != 0)
                {
                    throw new InvalidOperationException("Cannot rebase an entity with pending dirty properties.");
                }
                Sequence = sequence;
            }

            internal void ClearPending()
            {
                _properties.Clear();
                _pendingBytes = 0;
            }

            private static int GetPropertySize(int valueLength, bool component)
            {
                return checked(PropertyOverheadBytes + (component ? 2 : 1) * sizeof(ulong) + valueLength);
            }
        }
    }
}
