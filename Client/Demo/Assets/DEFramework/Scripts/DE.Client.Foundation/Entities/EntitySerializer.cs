using System;
using System.Collections.Generic;

namespace DE.Client.Entities
{
    /// <summary>属性树 v4 编解码入口；Snapshot 保存完整状态，Patch 仅携带各脏属性的最新值。</summary>
    public static class EntitySerializer
    {
        private const ushort CurrentVersion = 4;
        private const byte ObjectTag = 0;

        public static byte[] SerializeSnapshot(ClientEntity entity, EntitySerializationProfile profile)
        {
            RequireInitialized(entity);
            ValidateProfile(profile);
            var node = ValidateTree(entity);
            byte[] body = WriteNodeBody(node, profile);
            return WritePacket(profile, EntitySerializationMode.Snapshot, 0, 0, body);
        }

        public static void RestoreSnapshot(
            ClientEntity entity, EntitySerializationProfile profile, byte[] data, Guid? expectedEntityId = null)
        {
            if (!TryReadSnapshot(entity, profile, data, expectedEntityId, true))
            {
                throw new InvalidOperationException("The entity initialization snapshot is invalid.");
            }
        }

        public static void ApplySnapshot(ClientEntity entity, EntitySerializationProfile profile, byte[] data)
        {
            if (!TryApplySnapshot(entity, profile, data))
            {
                throw new InvalidOperationException("The entity snapshot is invalid.");
            }
        }

        public static bool TryApplySnapshot(ClientEntity entity, EntitySerializationProfile profile, byte[] data)
        {
            return TryReadSnapshot(entity, profile, data, entity?.Guid, false);
        }

        public static bool TryApplyPatch(ClientEntity entity, EntitySerializationProfile profile, byte[] data)
        {
            if (entity == null || !entity.IsInitialized || !IsValidProfile(profile)
                || profile == EntitySerializationProfile.Migration || !TryOwnPacket(data, out var packet))
            {
                return false;
            }
            var node = ValidateTree(entity);
            if (!ReadHeader(packet, profile, EntitySerializationMode.Patch, out var body, out ulong start, out ulong end))
            {
                return false;
            }
            var validation = body;
            var context = new EntityPropertyReadContext(profile, EntitySerializationMode.Patch, false, entity.Guid);
            if (!ReadPatchBody(node, ref validation, context, null))
            {
                return false;
            }
            var applied = new List<EntityPropertyChange>();
            context = new EntityPropertyReadContext(profile, EntitySerializationMode.Patch, true, entity.Guid);
            if (!ReadPatchBody(node, ref body, context, applied))
            {
                throw new InvalidOperationException("The generated property reader changed after validation.");
            }
            if (applied.Count != 0)
            {
                entity.NotifyPropertiesApplied(new EntityPropertyApplyContext(
                    profile, EntitySerializationMode.Patch, start, end, applied));
            }
            return true;
        }

        // Compatibility entry points retain explicit Profile semantics; production callers use the lifecycle-specific APIs above.
        public static byte[] Serialize(ClientEntity entity, EntitySerializationProfile profile) => SerializeSnapshot(entity, profile);

        public static void Deserialize(ClientEntity entity, EntitySerializationProfile profile, byte[] data)
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

        public static bool TryDeserialize(ClientEntity entity, EntitySerializationProfile profile, byte[] data)
        {
            return entity?.LifecycleState == EntityLifecycleState.Initializing
                ? TryReadSnapshot(entity, profile, data, null, true)
                : TryApplySnapshot(entity, profile, data);
        }

        public static bool TryDeserialize(
            ClientEntity entity, EntitySerializationProfile profile, byte[] data, int offset, int dataSize)
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

        public static void ValidateComponentBinding(ClientEntity owner, ClientEntityComponent component, ulong propertyId)
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
            ClientEntity entity, EntitySerializationProfile profile, byte[] data, Guid? expectedEntityId, bool initializing)
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

        private static bool ReadPatchBody(
            IEntityPropertyNode node, ref EntityPropertyReader reader, EntityPropertyReadContext context,
            List<EntityPropertyChange> applied)
        {
            if (!reader.TryReadUInt32(out uint count) || count > EntitySerializationLimits.MaximumEntries)
            {
                return false;
            }
            for (uint index = 0; index < count; index++)
            {
                if (!reader.TryReadUInt64(out ulong sequence)
                    || !ReadChange(ref reader, sequence, context.Profile, out var change))
                {
                    return false;
                }
                byte[] entry = EncodeChangeEntry(change);
                var entryReader = new EntityPropertyReader(entry, 0, entry.Length);
                if (!ReadEntry(ref entryReader, out ulong id, out byte tag, out var value) || entryReader.Remaining != 0)
                {
                    return false;
                }
                context.ResetPropertyRecognition();
                // Apply the current values in this packet, including repeats; records do not represent an assignment history.
                if (!node.ReadProperty(id, tag, ref value, context) || value.Remaining != 0)
                {
                    return false;
                }
                if (context.Apply && context.PropertyRecognized)
                {
                    applied.Add(change);
                }
            }
            return reader.Remaining == 0;
        }

        private static bool ReadChange(
            ref EntityPropertyReader reader, ulong sequence, EntitySerializationProfile profile, out EntityPropertyChange change)
        {
            change = null;
            var path = new List<ulong>();
            if (!ReadEntry(ref reader, out ulong id, out byte tag, out var value))
            {
                return false;
            }
            path.Add(id);
            while (tag == ObjectTag)
            {
                if (path.Count >= EntitySerializationLimits.MaximumDepth
                    || !value.TryReadUInt16(out ushort count) || count != 1
                    || !ReadEntry(ref value, out id, out tag, out var nested) || value.Remaining != 0)
                {
                    return false;
                }
                path.Add(id);
                value = nested;
            }
            change = new EntityPropertyChange(sequence, path, tag, value.ToArray(), ProfileMask(profile));
            return true;
        }

        private static byte[] EncodeChangeEntry(EntityPropertyChange change)
        {
            if (change.Path.Count == 0 || change.Path.Count > EntitySerializationLimits.MaximumDepth || change.TypeCode == ObjectTag)
            {
                throw new InvalidOperationException("A property change must contain one scalar leaf path.");
            }
            var leaf = new EntityPropertyWriter();
            leaf.WritePropertyHeader(change.Path[change.Path.Count - 1], change.TypeCode, change.EncodedValue.Length);
            leaf.WriteBytes(change.EncodedValue);
            byte[] entry = leaf.ToArray();
            for (int index = change.Path.Count - 2; index >= 0; index--)
            {
                var parent = new EntityPropertyWriter();
                parent.WritePropertyHeader(change.Path[index], ObjectTag, checked(sizeof(ushort) + entry.Length));
                parent.WriteUInt16(1);
                parent.WriteBytes(entry);
                entry = parent.ToArray();
            }
            return entry;
        }

        private static IEntityPropertyNode ValidateTree(ClientEntity entity)
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

        private static void RequireInitialized(ClientEntity entity)
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
