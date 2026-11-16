using System;
using System.Collections.Generic;
using System.Text;
using DE.Share.Foundation.Mathematics;

namespace DE.Client.Entities
{
    public enum EntitySerializeReason : byte
    {
        OwnerSync = 1,
        Broadcase = 2,
        Migrate = 3,
    }

    public static class EntitySerializer
    {
        private const ushort CurrentVersion = 3;
        private static readonly ulong EntityComponentId = EntityNameHash.Compute(string.Empty);

        public static byte[] Serialize(ClientEntity entity, EntitySerializeReason reason)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }
            if (!entity.IsInitialized)
            {
                throw new InvalidOperationException("Entity must complete initialization before serialization.");
            }
            ValidateReason(reason);

            var groups = new List<SelectedPropertyGroup>();
            int payloadSize = 0;
            foreach (var pair in GetPropertyOwners(entity))
            {
                var properties = new List<EntityPropertyMetadata>();
                int groupSize = sizeof(ushort);
                foreach (var property in pair.Value.Metadata.Properties)
                {
                    if (!CanSerialize(property.Flags, reason))
                    {
                        continue;
                    }
                    properties.Add(property);
                    groupSize = checked(groupSize + sizeof(ulong) + sizeof(byte) + sizeof(uint)
                        + GetValueWireSize(property.ValueType, property.GetValue(pair.Value.Target)));
                }
                if (properties.Count == 0)
                {
                    continue;
                }
                payloadSize = checked(payloadSize + sizeof(ulong) + sizeof(uint) + groupSize);
                groups.Add(new SelectedPropertyGroup(pair.Key, pair.Value.Target, properties, groupSize));
            }
            groups.Sort((left, right) => left.ComponentId.CompareTo(right.ComponentId));

            var bytes = new byte[checked(sizeof(ushort) + sizeof(byte) + sizeof(ushort) + payloadSize)];
            int offset = 0;
            WriteUInt16(bytes, ref offset, CurrentVersion);
            WriteByte(bytes, ref offset, (byte)reason);
            WriteUInt16(bytes, ref offset, checked((ushort)groups.Count));
            foreach (var group in groups)
            {
                WriteUInt64(bytes, ref offset, group.ComponentId);
                WriteUInt32(bytes, ref offset, checked((uint)group.PayloadSize));
                WriteUInt16(bytes, ref offset, checked((ushort)group.Properties.Count));
                foreach (var property in group.Properties)
                {
                    object value = property.GetValue(group.Target);
                    WriteUInt64(bytes, ref offset, property.Id);
                    WriteByte(bytes, ref offset, GetValueTypeCode(property.ValueType));
                    WriteUInt32(bytes, ref offset, checked((uint)GetValueWireSize(property.ValueType, value)));
                    WriteValue(bytes, ref offset, property.ValueType, value);
                }
            }
            return bytes;
        }

        public static void Deserialize(ClientEntity entity, EntitySerializeReason reason, byte[] data)
        {
            if (!TryDeserialize(entity, reason, data, 0, data == null ? 0 : data.Length))
            {
                throw new InvalidOperationException("Entity data is invalid.");
            }
        }

        public static bool TryDeserialize(ClientEntity entity, EntitySerializeReason reason, byte[] data)
        {
            return TryDeserialize(entity, reason, data, 0, data == null ? 0 : data.Length);
        }

        public static bool TryDeserialize(ClientEntity entity, EntitySerializeReason reason, byte[] data, int offset, int dataSize)
        {
            if (entity == null || data == null || offset < 0 || dataSize < 0 || data.Length - offset < dataSize || !IsValidReason(reason))
            {
                return false;
            }
            int endOffset = offset + dataSize;
            if (!TryReadUInt16(data, ref offset, endOffset, out ushort version) || version != CurrentVersion
                || !TryReadByte(data, ref offset, endOffset, out byte serializedReason) || serializedReason != (byte)reason
                || !TryReadUInt16(data, ref offset, endOffset, out ushort groupCount))
            {
                return false;
            }

            var owners = GetPropertyOwners(entity);
            var seenGroups = new HashSet<ulong>();
            var assignments = new List<PropertyAssignment>();
            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                if (!TryReadUInt64(data, ref offset, endOffset, out ulong componentId) || !seenGroups.Add(componentId)
                    || !TryReadUInt32(data, ref offset, endOffset, out uint groupSize)
                    || groupSize < sizeof(ushort) || groupSize > (uint)(endOffset - offset))
                {
                    return false;
                }
                int groupEndOffset = offset + (int)groupSize;
                if (!owners.TryGetValue(componentId, out PropertyOwner owner))
                {
                    offset = groupEndOffset;
                    continue;
                }
                if (!TryReadUInt16(data, ref offset, groupEndOffset, out ushort propertyCount))
                {
                    return false;
                }

                var seenProperties = new HashSet<ulong>();
                for (int index = 0; index < propertyCount; index++)
                {
                    if (!TryReadUInt64(data, ref offset, groupEndOffset, out ulong id) || !seenProperties.Add(id)
                        || !TryReadByte(data, ref offset, groupEndOffset, out byte valueTypeCode)
                        || !TryReadUInt32(data, ref offset, groupEndOffset, out uint valueSize)
                        || valueSize > (uint)(groupEndOffset - offset))
                    {
                        return false;
                    }
                    int valueEndOffset = offset + (int)valueSize;
                    if (!owner.Metadata.TryGetProperty(id, out EntityPropertyMetadata property))
                    {
                        offset = valueEndOffset;
                        continue;
                    }
                    if (!CanSerialize(property.Flags, reason) || valueTypeCode != GetValueTypeCode(property.ValueType)
                        || !TryReadValue(data, ref offset, valueEndOffset, property.ValueType, out object value)
                        || offset != valueEndOffset)
                    {
                        return false;
                    }
                    assignments.Add(new PropertyAssignment(owner.Target, property, value));
                }
                if (offset != groupEndOffset)
                {
                    return false;
                }
            }
            if (offset != endOffset)
            {
                return false;
            }

            // Validate the entire snapshot before applying any field, including when unknown data is skipped.
            foreach (var assignment in assignments)
            {
                assignment.Property.SetValue(assignment.Target, assignment.Value);
            }
            return true;
        }

        private static Dictionary<ulong, PropertyOwner> GetPropertyOwners(ClientEntity entity)
        {
            // The entity uses the empty name. Other owners use their component-holding property name.
            var owners = new Dictionary<ulong, PropertyOwner>
            {
                { EntityComponentId, new PropertyOwner(string.Empty, entity, entity.PropertyMetadata) },
            };
            foreach (var component in entity.Components)
            {
                var metadata = component.PropertyMetadata;
                if (metadata.PropertyCount == 0)
                {
                    continue;
                }
                string name = component.OwnerPropertyName;
                if (string.IsNullOrEmpty(name))
                {
                    throw new InvalidOperationException("Entity component wire names must not be empty.");
                }
                ulong id = EntityNameHash.Compute(name);
                if (owners.TryGetValue(id, out var previous))
                {
                    throw new InvalidOperationException(
                        $"Component hash collision: '{previous.ComponentName}' and '{name}' (0x{id:X16}).");
                }
                owners.Add(id, new PropertyOwner(name, component, metadata));
            }
            return owners;
        }

        private static bool CanSerialize(EntityPropertyFlag flags, EntitySerializeReason reason)
        {
            switch (reason)
            {
            case EntitySerializeReason.OwnerSync:
                return flags.HasFlag(EntityPropertyFlag.ClientServer);
            case EntitySerializeReason.Broadcase:
                return flags.HasFlag(EntityPropertyFlag.AllClients);
            case EntitySerializeReason.Migrate:
                return flags.HasFlag(EntityPropertyFlag.ServerOnly)
                    || flags.HasFlag(EntityPropertyFlag.ClientServer)
                    || flags.HasFlag(EntityPropertyFlag.AllClients);
            default:
                return false;
            }
        }

        private static void ValidateReason(EntitySerializeReason reason)
        {
            if (!IsValidReason(reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unsupported entity serialize reason.");
            }
        }

        private static bool IsValidReason(EntitySerializeReason reason)
        {
            return reason == EntitySerializeReason.OwnerSync
                || reason == EntitySerializeReason.Broadcase
                || reason == EntitySerializeReason.Migrate;
        }

        private static int GetValueWireSize(Type valueType, object value)
        {
            if (valueType == typeof(Vector3))
            {
                return 3 * sizeof(float);
            }
            if (valueType == typeof(Quaternion))
            {
                return 4 * sizeof(float);
            }

            Type underlyingType = Nullable.GetUnderlyingType(valueType);
            if (underlyingType != null)
            {
                return sizeof(byte) + (value == null ? 0 : GetValueWireSize(underlyingType, value));
            }

            if (valueType.IsEnum)
            {
                return GetValueWireSize(Enum.GetUnderlyingType(valueType), value);
            }

            if (valueType == typeof(bool) || valueType == typeof(byte) || valueType == typeof(sbyte))
            {
                return sizeof(byte);
            }

            if (valueType == typeof(short) || valueType == typeof(ushort) || valueType == typeof(char))
            {
                return sizeof(ushort);
            }

            if (valueType == typeof(int) || valueType == typeof(uint) || valueType == typeof(float))
            {
                return sizeof(uint);
            }

            if (valueType == typeof(long) || valueType == typeof(ulong) || valueType == typeof(double))
            {
                return sizeof(ulong);
            }

            if (valueType == typeof(Guid))
            {
                return 16;
            }

            if (valueType == typeof(string))
            {
                int byteCount = value == null ? 0 : Encoding.UTF8.GetByteCount((string)value);
                if (byteCount > ushort.MaxValue)
                {
                    throw new InvalidOperationException("ClientEntity string property is too long.");
                }

                return sizeof(ushort) + byteCount;
            }

            if (valueType == typeof(byte[]))
            {
                int byteCount = value == null ? 0 : ((byte[])value).Length;
                if (byteCount > ushort.MaxValue)
                {
                    throw new InvalidOperationException("ClientEntity byte array property is too long.");
                }

                return sizeof(ushort) + byteCount;
            }

            throw new NotSupportedException("Unsupported entity property type: " + valueType.FullName);
        }

        private static byte GetValueTypeCode(Type valueType)
        {
            Type underlyingType = Nullable.GetUnderlyingType(valueType);
            if (underlyingType != null)
            {
                return unchecked((byte)(0x80 | GetValueTypeCode(underlyingType)));
            }

            if (valueType.IsEnum)
            {
                return unchecked((byte)(0x40 | GetValueTypeCode(Enum.GetUnderlyingType(valueType))));
            }

            if (valueType == typeof(bool))
            {
                return 1;
            }

            if (valueType == typeof(byte))
            {
                return 2;
            }

            if (valueType == typeof(sbyte))
            {
                return 3;
            }

            if (valueType == typeof(short))
            {
                return 4;
            }

            if (valueType == typeof(ushort))
            {
                return 5;
            }

            if (valueType == typeof(int))
            {
                return 6;
            }

            if (valueType == typeof(uint))
            {
                return 7;
            }

            if (valueType == typeof(long))
            {
                return 8;
            }

            if (valueType == typeof(ulong))
            {
                return 9;
            }

            if (valueType == typeof(float))
            {
                return 10;
            }

            if (valueType == typeof(double))
            {
                return 11;
            }

            if (valueType == typeof(string))
            {
                return 12;
            }

            if (valueType == typeof(Guid))
            {
                return 13;
            }

            if (valueType == typeof(char))
            {
                return 14;
            }

            if (valueType == typeof(byte[]))
            {
                return 15;
            }

            if (valueType == typeof(Vector3))
            {
                return 16;
            }

            if (valueType == typeof(Quaternion))
            {
                return 17;
            }

            throw new NotSupportedException("Unsupported entity property type: " + valueType.FullName);
        }

        private static void WriteValue(byte[] bytes, ref int offset, Type valueType, object value)
        {
            if (valueType == typeof(Vector3))
            {
                var vector = (Vector3)value;
                WriteSingle(bytes, ref offset, vector.X);
                WriteSingle(bytes, ref offset, vector.Y);
                WriteSingle(bytes, ref offset, vector.Z);
                return;
            }
            if (valueType == typeof(Quaternion))
            {
                var rotation = (Quaternion)value;
                WriteSingle(bytes, ref offset, rotation.X);
                WriteSingle(bytes, ref offset, rotation.Y);
                WriteSingle(bytes, ref offset, rotation.Z);
                WriteSingle(bytes, ref offset, rotation.W);
                return;
            }

            Type underlyingType = Nullable.GetUnderlyingType(valueType);
            if (underlyingType != null)
            {
                WriteByte(bytes, ref offset, value == null ? (byte)0 : (byte)1);
                if (value != null)
                {
                    WriteValue(bytes, ref offset, underlyingType, value);
                }

                return;
            }

            if (valueType.IsEnum)
            {
                WriteValue(bytes, ref offset, Enum.GetUnderlyingType(valueType), Convert.ChangeType(value, Enum.GetUnderlyingType(valueType)));
                return;
            }

            if (valueType == typeof(bool))
            {
                WriteByte(bytes, ref offset, (bool)value ? (byte)1 : (byte)0);
                return;
            }

            if (valueType == typeof(byte))
            {
                WriteByte(bytes, ref offset, (byte)value);
                return;
            }

            if (valueType == typeof(sbyte))
            {
                WriteByte(bytes, ref offset, unchecked((byte)(sbyte)value));
                return;
            }

            if (valueType == typeof(short))
            {
                WriteUInt16(bytes, ref offset, unchecked((ushort)(short)value));
                return;
            }

            if (valueType == typeof(ushort))
            {
                WriteUInt16(bytes, ref offset, (ushort)value);
                return;
            }

            if (valueType == typeof(int))
            {
                WriteUInt32(bytes, ref offset, unchecked((uint)(int)value));
                return;
            }

            if (valueType == typeof(uint))
            {
                WriteUInt32(bytes, ref offset, (uint)value);
                return;
            }

            if (valueType == typeof(long))
            {
                WriteUInt64(bytes, ref offset, unchecked((ulong)(long)value));
                return;
            }

            if (valueType == typeof(ulong))
            {
                WriteUInt64(bytes, ref offset, (ulong)value);
                return;
            }

            if (valueType == typeof(float))
            {
                WriteSingle(bytes, ref offset, (float)value);
                return;
            }

            if (valueType == typeof(double))
            {
                WriteUInt64(bytes, ref offset, unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)));
                return;
            }

            if (valueType == typeof(string))
            {
                WriteString(bytes, ref offset, (string)value);
                return;
            }

            if (valueType == typeof(Guid))
            {
                WriteGuid(bytes, ref offset, (Guid)value);
                return;
            }

            if (valueType == typeof(char))
            {
                WriteUInt16(bytes, ref offset, (char)value);
                return;
            }

            if (valueType == typeof(byte[]))
            {
                WriteByteArray(bytes, ref offset, (byte[])value);
                return;
            }

            throw new NotSupportedException("Unsupported entity property type: " + valueType.FullName);
        }

        private static bool TryReadValue(byte[] bytes, ref int offset, int endOffset, Type valueType, out object value)
        {
            value = null;

            if (valueType == typeof(Vector3))
            {
                if (!TryReadSingle(bytes, ref offset, endOffset, out float x)
                    || !TryReadSingle(bytes, ref offset, endOffset, out float y)
                    || !TryReadSingle(bytes, ref offset, endOffset, out float z))
                {
                    return false;
                }
                value = new Vector3(x, y, z);
                return true;
            }
            if (valueType == typeof(Quaternion))
            {
                if (!TryReadSingle(bytes, ref offset, endOffset, out float x)
                    || !TryReadSingle(bytes, ref offset, endOffset, out float y)
                    || !TryReadSingle(bytes, ref offset, endOffset, out float z)
                    || !TryReadSingle(bytes, ref offset, endOffset, out float w))
                {
                    return false;
                }
                value = new Quaternion(x, y, z, w);
                return true;
            }

            Type underlyingType = Nullable.GetUnderlyingType(valueType);
            if (underlyingType != null)
            {
                if (!TryReadByte(bytes, ref offset, endOffset, out byte hasValue))
                {
                    return false;
                }

                if (hasValue == 0)
                {
                    value = null;
                    return true;
                }

                if (hasValue != 1)
                {
                    return false;
                }

                return TryReadValue(bytes, ref offset, endOffset, underlyingType, out value);
            }

            if (valueType.IsEnum)
            {
                if (!TryReadValue(bytes, ref offset, endOffset, Enum.GetUnderlyingType(valueType), out object rawValue))
                {
                    return false;
                }

                value = Enum.ToObject(valueType, rawValue);
                return true;
            }

            if (valueType == typeof(bool))
            {
                if (!TryReadByte(bytes, ref offset, endOffset, out byte boolValue) || boolValue > 1)
                {
                    return false;
                }

                value = boolValue != 0;
                return true;
            }

            if (valueType == typeof(byte))
            {
                if (!TryReadByte(bytes, ref offset, endOffset, out byte byteValue))
                {
                    return false;
                }

                value = byteValue;
                return true;
            }

            if (valueType == typeof(sbyte))
            {
                if (!TryReadByte(bytes, ref offset, endOffset, out byte sbyteValue))
                {
                    return false;
                }

                value = unchecked((sbyte)sbyteValue);
                return true;
            }

            if (valueType == typeof(short))
            {
                if (!TryReadUInt16(bytes, ref offset, endOffset, out ushort shortValue))
                {
                    return false;
                }

                value = unchecked((short)shortValue);
                return true;
            }

            if (valueType == typeof(ushort))
            {
                if (!TryReadUInt16(bytes, ref offset, endOffset, out ushort ushortValue))
                {
                    return false;
                }

                value = ushortValue;
                return true;
            }

            if (valueType == typeof(int))
            {
                if (!TryReadUInt32(bytes, ref offset, endOffset, out uint intValue))
                {
                    return false;
                }

                value = unchecked((int)intValue);
                return true;
            }

            if (valueType == typeof(uint))
            {
                if (!TryReadUInt32(bytes, ref offset, endOffset, out uint uintValue))
                {
                    return false;
                }

                value = uintValue;
                return true;
            }

            if (valueType == typeof(long))
            {
                if (!TryReadUInt64(bytes, ref offset, endOffset, out ulong longValue))
                {
                    return false;
                }

                value = unchecked((long)longValue);
                return true;
            }

            if (valueType == typeof(ulong))
            {
                if (!TryReadUInt64(bytes, ref offset, endOffset, out ulong ulongValue))
                {
                    return false;
                }

                value = ulongValue;
                return true;
            }

            if (valueType == typeof(float))
            {
                if (!TryReadSingle(bytes, ref offset, endOffset, out float floatValue))
                {
                    return false;
                }

                value = floatValue;
                return true;
            }

            if (valueType == typeof(double))
            {
                if (!TryReadUInt64(bytes, ref offset, endOffset, out ulong doubleValue))
                {
                    return false;
                }

                value = BitConverter.Int64BitsToDouble(unchecked((long)doubleValue));
                return true;
            }

            if (valueType == typeof(string))
            {
                return TryReadString(bytes, ref offset, endOffset, out value);
            }

            if (valueType == typeof(Guid))
            {
                if (!TryReadGuid(bytes, ref offset, endOffset, out Guid guidValue))
                {
                    return false;
                }

                value = guidValue;
                return true;
            }

            if (valueType == typeof(char))
            {
                if (!TryReadUInt16(bytes, ref offset, endOffset, out ushort charValue))
                {
                    return false;
                }

                value = (char)charValue;
                return true;
            }

            if (valueType == typeof(byte[]))
            {
                return TryReadByteArray(bytes, ref offset, endOffset, out value);
            }

            return false;
        }

        private static void WriteSingle(byte[] bytes, ref int offset, float value)
        {
            WriteUInt32(bytes, ref offset, unchecked((uint)BitConverter.ToInt32(BitConverter.GetBytes(value), 0)));
        }

        private static bool TryReadSingle(byte[] bytes, ref int offset, int endOffset, out float value)
        {
            value = default;
            if (!TryReadUInt32(bytes, ref offset, endOffset, out uint bits))
            {
                return false;
            }
            value = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
            return true;
        }

        private static void WriteString(byte[] bytes, ref int offset, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteUInt16(bytes, ref offset, 0);
                return;
            }

            byte[] valueBytes = Encoding.UTF8.GetBytes(value);
            WriteUInt16(bytes, ref offset, checked((ushort)valueBytes.Length));
            Buffer.BlockCopy(valueBytes, 0, bytes, offset, valueBytes.Length);
            offset += valueBytes.Length;
        }

        private static bool TryReadString(byte[] bytes, ref int offset, int endOffset, out object value)
        {
            value = null;
            if (!TryReadUInt16(bytes, ref offset, endOffset, out ushort length) || endOffset - offset < length)
            {
                return false;
            }

            value = length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes, offset, length);
            offset += length;
            return true;
        }

        private static void WriteByteArray(byte[] bytes, ref int offset, byte[] value)
        {
            if (value == null || value.Length == 0)
            {
                WriteUInt16(bytes, ref offset, 0);
                return;
            }

            WriteUInt16(bytes, ref offset, checked((ushort)value.Length));
            Buffer.BlockCopy(value, 0, bytes, offset, value.Length);
            offset += value.Length;
        }

        private static bool TryReadByteArray(byte[] bytes, ref int offset, int endOffset, out object value)
        {
            value = null;
            if (!TryReadUInt16(bytes, ref offset, endOffset, out ushort length) || endOffset - offset < length)
            {
                return false;
            }

            byte[] valueBytes = new byte[length];
            if (length > 0)
            {
                Buffer.BlockCopy(bytes, offset, valueBytes, 0, length);
            }

            offset += length;
            value = valueBytes;
            return true;
        }

        private static void WriteGuid(byte[] bytes, ref int offset, Guid value)
        {
            byte[] guidBytes = value.ToByteArray();
            Buffer.BlockCopy(guidBytes, 0, bytes, offset, guidBytes.Length);
            offset += guidBytes.Length;
        }

        private static bool TryReadGuid(byte[] bytes, ref int offset, int endOffset, out Guid value)
        {
            value = Guid.Empty;
            if (endOffset - offset < 16)
            {
                return false;
            }

            byte[] guidBytes = new byte[16];
            Buffer.BlockCopy(bytes, offset, guidBytes, 0, guidBytes.Length);
            offset += guidBytes.Length;
            value = new Guid(guidBytes);
            return true;
        }

        private static void WriteByte(byte[] bytes, ref int offset, byte value)
        {
            bytes[offset] = value;
            offset += sizeof(byte);
        }

        private static bool TryReadByte(byte[] bytes, ref int offset, int endOffset, out byte value)
        {
            value = 0;
            if (endOffset - offset < sizeof(byte))
            {
                return false;
            }

            value = bytes[offset];
            offset += sizeof(byte);
            return true;
        }

        private static void WriteUInt16(byte[] bytes, ref int offset, ushort value)
        {
            bytes[offset] = (byte)((value >> 8) & 0xff);
            bytes[offset + 1] = (byte)(value & 0xff);
            offset += sizeof(ushort);
        }

        private static bool TryReadUInt16(byte[] bytes, ref int offset, int endOffset, out ushort value)
        {
            value = 0;
            if (endOffset - offset < sizeof(ushort))
            {
                return false;
            }

            value = (ushort)(((ushort)bytes[offset] << 8) | bytes[offset + 1]);
            offset += sizeof(ushort);
            return true;
        }

        private static void WriteUInt32(byte[] bytes, ref int offset, uint value)
        {
            bytes[offset] = (byte)((value >> 24) & 0xff);
            bytes[offset + 1] = (byte)((value >> 16) & 0xff);
            bytes[offset + 2] = (byte)((value >> 8) & 0xff);
            bytes[offset + 3] = (byte)(value & 0xff);
            offset += sizeof(uint);
        }

        private static bool TryReadUInt32(byte[] bytes, ref int offset, int endOffset, out uint value)
        {
            value = 0;
            if (endOffset - offset < sizeof(uint))
            {
                return false;
            }

            value = ((uint)bytes[offset] << 24)
                | ((uint)bytes[offset + 1] << 16)
                | ((uint)bytes[offset + 2] << 8)
                | bytes[offset + 3];
            offset += sizeof(uint);
            return true;
        }

        private static void WriteUInt64(byte[] bytes, ref int offset, ulong value)
        {
            bytes[offset] = (byte)((value >> 56) & 0xff);
            bytes[offset + 1] = (byte)((value >> 48) & 0xff);
            bytes[offset + 2] = (byte)((value >> 40) & 0xff);
            bytes[offset + 3] = (byte)((value >> 32) & 0xff);
            bytes[offset + 4] = (byte)((value >> 24) & 0xff);
            bytes[offset + 5] = (byte)((value >> 16) & 0xff);
            bytes[offset + 6] = (byte)((value >> 8) & 0xff);
            bytes[offset + 7] = (byte)(value & 0xff);
            offset += sizeof(ulong);
        }

        private static bool TryReadUInt64(byte[] bytes, ref int offset, int endOffset, out ulong value)
        {
            value = 0;
            if (endOffset - offset < sizeof(ulong))
            {
                return false;
            }

            value = ((ulong)bytes[offset] << 56)
                | ((ulong)bytes[offset + 1] << 48)
                | ((ulong)bytes[offset + 2] << 40)
                | ((ulong)bytes[offset + 3] << 32)
                | ((ulong)bytes[offset + 4] << 24)
                | ((ulong)bytes[offset + 5] << 16)
                | ((ulong)bytes[offset + 6] << 8)
                | bytes[offset + 7];
            offset += sizeof(ulong);
            return true;
        }

        private sealed class PropertyOwner
        {
            public PropertyOwner(string componentName, object target, EntityClassMetadata metadata)
            {
                ComponentName = componentName;
                Target = target;
                Metadata = metadata;
            }

            public string ComponentName { get; }
            public object Target { get; }
            public EntityClassMetadata Metadata { get; }
        }

        private sealed class SelectedPropertyGroup
        {
            public SelectedPropertyGroup(ulong componentId, object target, List<EntityPropertyMetadata> properties, int payloadSize)
            {
                ComponentId = componentId;
                Target = target;
                Properties = properties;
                PayloadSize = payloadSize;
            }

            public ulong ComponentId { get; }
            public object Target { get; }
            public List<EntityPropertyMetadata> Properties { get; }
            public int PayloadSize { get; }
        }

        private readonly struct PropertyAssignment
        {
            public PropertyAssignment(object target, EntityPropertyMetadata property, object value)
            {
                Target = target;
                Property = property;
                Value = value;
            }

            public object Target { get; }
            public EntityPropertyMetadata Property { get; }
            public object Value { get; }
        }
    }
}
