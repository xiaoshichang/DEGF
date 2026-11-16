using System;
using System.Collections.Generic;
using System.Linq;

namespace DE.Server.Entities
{
    /// <summary>属性树 v4 编解码入口；Snapshot 保存完整状态，Patch 仅携带各脏属性的最新值。</summary>
    public static class EntitySerializer
    {
        private const ushort CurrentVersion = 4;
        private const byte ObjectTag = 0;

        public static byte[] SerializeSnapshot(ServerEntity entity, EntitySerializationProfile profile)
        {
            RequireInitialized(entity);
            ValidateProfile(profile);
            var node = ValidateTree(entity);
            ulong sequence = entity.PropertySequence;
            byte[] body = WriteNodeBody(node, profile);
            if (sequence != entity.PropertySequence)
            {
                throw new InvalidOperationException("Entity properties changed while serializing the snapshot.");
            }
            return WritePacket(profile, EntitySerializationMode.Snapshot, sequence, sequence, body);
        }

        public static byte[] SerializePatch(ServerEntity entity, EntitySerializationProfile profile)
        {
            RequireInitialized(entity);
            ValidateProfile(profile);
            if (profile == EntitySerializationProfile.Migration)
            {
                throw new ArgumentException("Migration requires a complete snapshot.", nameof(profile));
            }
            ValidateTree(entity);
            var changes = entity.PendingDirtyProperties;
            var selected = new List<ServerEntity.DirtyProperty>();
            var mask = ProfileMask(profile);
            foreach (var change in changes)
            {
                if ((change.Profiles & mask) != 0)
                {
                    selected.Add(change);
                }
            }

            var body = new EntityPropertyWriter();
            body.WriteUInt32(checked((uint)selected.Count));
            foreach (var change in selected)
            {
                body.WriteUInt64(change.Sequence);
                body.WriteBytes(EncodeDirtyPropertyEntry(entity, profile, change));
            }
            ulong throughSequence = entity.PropertySequence;
            ulong baseSequence = changes.Count == 0 ? throughSequence : changes.Min(property => property.Sequence) - 1;
            return WritePacket(profile, EntitySerializationMode.Patch, baseSequence, throughSequence, body.ToArray());
        }

        public static void RestoreSnapshot(
            ServerEntity entity, EntitySerializationProfile profile, byte[] data, Guid? expectedEntityId = null)
        {
            if (!TryReadSnapshot(entity, profile, data, expectedEntityId, true))
            {
                throw new InvalidOperationException("The entity initialization snapshot is invalid.");
            }
        }

        public static void ApplySnapshot(ServerEntity entity, EntitySerializationProfile profile, byte[] data)
        {
            if (!TryApplySnapshot(entity, profile, data))
            {
                throw new InvalidOperationException("The entity snapshot is invalid.");
            }
        }

        public static bool TryApplySnapshot(ServerEntity entity, EntitySerializationProfile profile, byte[] data)
        {
            return TryReadSnapshot(entity, profile, data, entity?.Guid, false);
        }

        // Compatibility entry points retain explicit Profile semantics; production callers use the lifecycle-specific APIs above.
        public static byte[] Serialize(ServerEntity entity, EntitySerializationProfile profile) => SerializeSnapshot(entity, profile);

        public static void Deserialize(ServerEntity entity, EntitySerializationProfile profile, byte[] data)
        {
            if (entity?.LifecycleState == EntityLifecycleState.Initializing)
            {
                RestoreSnapshot(entity, profile, data);
            }
            else
            {
                ApplySnapshot(entity, profile, data);
            }
        }

        public static bool TryDeserialize(ServerEntity entity, EntitySerializationProfile profile, byte[] data)
        {
            return entity?.LifecycleState == EntityLifecycleState.Initializing
                ? TryReadSnapshot(entity, profile, data, null, true)
                : TryApplySnapshot(entity, profile, data);
        }

        public static bool TryDeserialize(
            ServerEntity entity, EntitySerializationProfile profile, byte[] data, int offset, int dataSize)
        {
            if (data == null || offset < 0 || dataSize < 0 || data.Length - offset < dataSize
                || dataSize > EntitySerializationLimits.MaximumPacketBytes)
            {
                return false;
            }
            var slice = new byte[dataSize];
            Buffer.BlockCopy(data, offset, slice, 0, dataSize);
            return TryDeserialize(entity, profile, slice);
        }

        public static void WriteChildNode(
            EntityPropertyWriter writer, ulong propertyId, IEntityPropertyNode child, EntitySerializationProfile profile)
        {
            if (child == null)
            {
                throw new InvalidOperationException("A declared component node has not been constructed.");
            }
            if (!child.HasSnapshotProperties(profile))
            {
                return;
            }
            byte[] body = WriteNodeBody(child, profile);
            writer.WritePropertyHeader(propertyId, ObjectTag, body.Length);
            writer.WriteBytes(body);
        }

        public static bool ReadNodeBody(
            IEntityPropertyNode node, ref EntityPropertyReader reader, EntityPropertyReadContext context)
        {
            if (context.Depth >= EntitySerializationLimits.MaximumDepth)
            {
                return false;
            }
            context.Enter();
            try
            {
                if (!reader.TryReadUInt16(out ushort count))
                {
                    return false;
                }
                var seen = new HashSet<ulong>();
                for (int index = 0; index < count; index++)
                {
                    if (!ReadEntry(ref reader, out ulong id, out byte tag, out var value)
                        || !seen.Add(id) || !node.ReadProperty(id, tag, ref value, context) || value.Remaining != 0)
                    {
                        return false;
                    }
                }
                return reader.Remaining == 0 && (context.Mode != EntitySerializationMode.Snapshot
                    || node.ValidateSnapshot(seen, context.Profile));
            }
            finally
            {
                context.Exit();
            }
        }

        public static void ValidateComponentBinding(ServerEntity owner, ServerEntityComponent component, ulong propertyId)
        {
            if (owner == null || component == null || !ReferenceEquals(component.OwnerEntity, owner)
                || !((IEntityPropertyNode)owner).TryGetComponentPropertyHash(component, out ulong propertyHash)
                || propertyHash != propertyId)
            {
                throw new InvalidOperationException("A declared component node does not match its mounted owning property.");
            }
            bool mounted = false;
            foreach (var existing in owner.Components)
            {
                if (ReferenceEquals(existing, component))
                {
                    mounted = true;
                    break;
                }
            }
            if (!mounted)
            {
                throw new InvalidOperationException("A declared component node is not in the entity component collection.");
            }
            ((IEntityPropertyNode)component).ValidatePropertyTree();
        }

        private static bool TryReadSnapshot(
            ServerEntity entity, EntitySerializationProfile profile, byte[] data, Guid? expectedEntityId, bool initializing)
        {
            if (entity == null || !IsValidProfile(profile) || !TryOwnPacket(data, out var packet)
                || (initializing ? entity.LifecycleState != EntityLifecycleState.Initializing : !entity.IsInitialized))
            {
                return false;
            }
            var node = ValidateTree(entity);
            if (!ReadHeader(packet, profile, EntitySerializationMode.Snapshot, out var body, out ulong start, out ulong end)
                || start != end)
            {
                return false;
            }
            var validation = body;
            var context = new EntityPropertyReadContext(profile, EntitySerializationMode.Snapshot, false, expectedEntityId);
            if (!ReadNodeBody(node, ref validation, context))
            {
                return false;
            }
            context = new EntityPropertyReadContext(profile, EntitySerializationMode.Snapshot, true, expectedEntityId);
            if (!ReadNodeBody(node, ref body, context))
            {
                throw new InvalidOperationException("The generated property reader changed after snapshot validation.");
            }
            if (initializing && profile == EntitySerializationProfile.Migration)
            {
                entity.RebasePropertySequence(end);
            }
            if (!initializing)
            {
                entity.NotifyPropertiesApplied(new EntityPropertyApplyContext(
                    profile, EntitySerializationMode.Snapshot, start, end, Array.Empty<EntityPropertyChange>()));
            }
            return true;
        }

        private static byte[] WriteNodeBody(IEntityPropertyNode node, EntitySerializationProfile profile)
        {
            var entries = new EntityPropertyWriter();
            node.WriteSnapshot(entries, profile);
            var writer = new EntityPropertyWriter();
            writer.WriteUInt16(entries.PropertyCount);
            writer.WriteBytes(entries.ToArray());
            return writer.ToArray();
        }

        private static byte[] WritePacket(
            EntitySerializationProfile profile, EntitySerializationMode mode, ulong start, ulong end, byte[] body)
        {
            var writer = new EntityPropertyWriter();
            writer.WriteUInt16(CurrentVersion);
            writer.WriteByte((byte)profile);
            writer.WriteByte((byte)mode);
            writer.WriteUInt32(checked((uint)body.Length));
            writer.WriteUInt64(start);
            writer.WriteUInt64(end);
            writer.WriteBytes(body);
            return writer.ToArray();
        }

        private static bool ReadHeader(
            byte[] packet, EntitySerializationProfile profile, EntitySerializationMode mode,
            out EntityPropertyReader body, out ulong start, out ulong end)
        {
            body = default;
            start = 0;
            end = 0;
            var reader = new EntityPropertyReader(packet, 0, packet.Length);
            return reader.TryReadUInt16(out ushort version) && version == CurrentVersion
                && reader.TryReadByte(out byte wireProfile) && wireProfile == (byte)profile
                && reader.TryReadByte(out byte wireMode) && wireMode == (byte)mode
                && reader.TryReadUInt32(out uint size) && reader.TryReadUInt64(out start) && reader.TryReadUInt64(out end)
                && start <= end && size == (uint)reader.Remaining && reader.TrySlice(size, out body);
        }

        private static bool ReadEntry(ref EntityPropertyReader reader, out ulong id, out byte tag, out EntityPropertyReader value)
        {
            id = 0;
            tag = 0;
            value = default;
            return reader.TryReadUInt64(out id) && reader.TryReadByte(out tag) && reader.TryReadUInt32(out uint size)
                && size <= (uint)(tag == ObjectTag ? EntitySerializationLimits.MaximumPacketBytes : EntitySerializationLimits.MaximumValueBytes)
                && reader.TrySlice(size, out value);
        }

        private static byte[] EncodeDirtyPropertyEntry(
            ServerEntity entity, EntitySerializationProfile profile, ServerEntity.DirtyProperty property)
        {
            var writer = new EntityPropertyWriter();
            IEntityPropertyNode node = property.Component == null ? (IEntityPropertyNode)entity : property.Component;
            if (!node.WriteProperty(property.PropertyId, writer, profile))
            {
                throw new InvalidOperationException("The generated property writer does not recognize a marked dirty property.");
            }
            byte[] entry = writer.ToArray();
            if (!property.ComponentHash.HasValue)
            {
                return entry;
            }
            var parent = new EntityPropertyWriter();
            parent.WritePropertyHeader(property.ComponentHash.Value, ObjectTag, checked(sizeof(ushort) + entry.Length));
            parent.WriteUInt16(1);
            parent.WriteBytes(entry);
            return parent.ToArray();
        }

        private static IEntityPropertyNode ValidateTree(ServerEntity entity)
        {
            var node = (IEntityPropertyNode)entity;
            node.ValidatePropertyTree();
            foreach (var component in entity.Components)
            {
                if (!node.TryGetComponentPropertyHash((IEntityPropertyNode)component, out _))
                {
                    throw new InvalidOperationException("A mounted component is missing its EntityComponent declaration.");
                }
            }
            return node;
        }

        private static bool TryOwnPacket(byte[] data, out byte[] packet)
        {
            packet = null;
            if (data == null || data.Length > EntitySerializationLimits.MaximumPacketBytes)
            {
                return false;
            }
            // Own the bytes across both passes; pooled or caller-owned buffers must not change between validation and commit.
            packet = (byte[])data.Clone();
            return true;
        }

        private static void RequireInitialized(ServerEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }
            if (!entity.IsInitialized)
            {
                throw new InvalidOperationException("Entity serialization requires completed initialization.");
            }
        }

        private static bool IsValidProfile(EntitySerializationProfile profile)
        {
            return profile == EntitySerializationProfile.Owner || profile == EntitySerializationProfile.Observer
                || profile == EntitySerializationProfile.Migration;
        }

        private static void ValidateProfile(EntitySerializationProfile profile)
        {
            if (!IsValidProfile(profile))
            {
                throw new ArgumentOutOfRangeException(nameof(profile));
            }
        }

        private static EntitySerializationProfileMask ProfileMask(EntitySerializationProfile profile)
        {
            return (EntitySerializationProfileMask)(1 << ((int)profile - 1));
        }
    }
}
