using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace DE.Share.Entities
{
    public enum EntitySerializeReason : byte
    {
        OwnerSync = 1,
        Broadcase = 2,
        Migrate = 3,
    }

    public static class EntitySerializer
    {
        private const ushort CurrentVersion = 1;

        private static readonly Dictionary<Type, EntityPropertyMember[]> s_Members =
            new Dictionary<Type, EntityPropertyMember[]>();

        public static byte[] Serialize(Entity entity, EntitySerializeReason reason)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            ValidateReason(reason);

            EntityPropertyAccessor[] accessors = GetAccessors(entity);
            List<EntityPropertyAccessor> selectedAccessors = new List<EntityPropertyAccessor>(accessors.Length);
            int payloadSize = 0;
            for (int i = 0; i < accessors.Length; i++)
            {
                EntityPropertyAccessor accessor = accessors[i];
                if (!CanSerialize(accessor.Attribute, reason))
                {
                    continue;
                }

                selectedAccessors.Add(accessor);
                object value = accessor.GetValue(entity);
                payloadSize += sizeof(ushort) + sizeof(byte) + GetValueWireSize(accessor.ValueType, value);
            }

            byte[] bytes = new byte[sizeof(ushort) + sizeof(byte) + sizeof(ushort) + payloadSize];
            int offset = 0;
            WriteUInt16(bytes, ref offset, CurrentVersion);
            WriteByte(bytes, ref offset, (byte)reason);
            WriteUInt16(bytes, ref offset, checked((ushort)selectedAccessors.Count));

            for (int i = 0; i < selectedAccessors.Count; i++)
            {
                EntityPropertyAccessor accessor = selectedAccessors[i];
                WriteUInt16(bytes, ref offset, accessor.PropertyId);
                WriteByte(bytes, ref offset, GetValueTypeCode(accessor.ValueType));
                WriteValue(bytes, ref offset, accessor.ValueType, accessor.GetValue(entity));
            }

            return bytes;
        }

        public static void Deserialize(Entity entity, EntitySerializeReason reason, byte[] data)
        {
            if (!TryDeserialize(entity, reason, data, 0, data == null ? 0 : data.Length))
            {
                throw new InvalidOperationException("Entity data is invalid.");
            }
        }

        public static bool TryDeserialize(Entity entity, EntitySerializeReason reason, byte[] data)
        {
            return TryDeserialize(entity, reason, data, 0, data == null ? 0 : data.Length);
        }

        public static bool TryDeserialize(Entity entity, EntitySerializeReason reason, byte[] data, int offset, int dataSize)
        {
            if (entity == null || data == null || offset < 0 || dataSize < 0 || data.Length - offset < dataSize)
            {
                return false;
            }

            if (!IsValidReason(reason))
            {
                return false;
            }

            int endOffset = offset + dataSize;
            if (!TryReadUInt16(data, ref offset, endOffset, out ushort version) || version != CurrentVersion)
            {
                return false;
            }

            if (!TryReadByte(data, ref offset, endOffset, out byte serializedReason) || serializedReason != (byte)reason)
            {
                return false;
            }

            if (!TryReadUInt16(data, ref offset, endOffset, out ushort propertyCount))
            {
                return false;
            }

            Dictionary<ushort, EntityPropertyAccessor> accessors = GetAccessorsById(entity);
            for (int i = 0; i < propertyCount; i++)
            {
                if (!TryReadUInt16(data, ref offset, endOffset, out ushort propertyId)
                    || !TryReadByte(data, ref offset, endOffset, out byte valueTypeCode))
                {
                    return false;
                }

                if (!accessors.TryGetValue(propertyId, out EntityPropertyAccessor accessor)
                    || !CanSerialize(accessor.Attribute, reason)
                    || valueTypeCode != GetValueTypeCode(accessor.ValueType))
                {
                    return false;
                }

                if (!TryReadValue(data, ref offset, endOffset, accessor.ValueType, out object value))
                {
                    return false;
                }

                accessor.SetValue(entity, value);
            }

            return offset == endOffset;
        }

        private static EntityPropertyAccessor[] GetAccessors(Entity entity)
        {
            List<EntityPropertyAccessor> accessors = new List<EntityPropertyAccessor>();
            EntityPropertyMember[] entityMembers = GetMembers(entity.GetType(), string.Empty);
            for (int i = 0; i < entityMembers.Length; i++)
            {
                accessors.Add(entityMembers[i].CreateAccessor(entity));
            }

            IReadOnlyList<EntityComponent> components = entity.Components;
            for (int i = 0; i < components.Count; i++)
            {
                EntityComponent component = components[i];
                EntityPropertyMember[] componentMembers = GetMembers(component.GetType(), GetComponentPropertyScope(component.GetType()));
                for (int memberIndex = 0; memberIndex < componentMembers.Length; memberIndex++)
                {
                    accessors.Add(componentMembers[memberIndex].CreateAccessor(component));
                }
            }

            accessors.Sort(CompareAccessor);
            EnsureUniquePropertyIds(entity.GetType(), accessors);
            return accessors.ToArray();
        }

        private static Dictionary<ushort, EntityPropertyAccessor> GetAccessorsById(Entity entity)
        {
            EntityPropertyAccessor[] accessors = GetAccessors(entity);
            Dictionary<ushort, EntityPropertyAccessor> accessorsById = new Dictionary<ushort, EntityPropertyAccessor>(accessors.Length);
            for (int i = 0; i < accessors.Length; i++)
            {
                accessorsById.Add(accessors[i].PropertyId, accessors[i]);
            }

            return accessorsById;
        }

        private static EntityPropertyMember[] GetMembers(Type ownerType, string propertyScope)
        {
            lock (s_Members)
            {
                if (!s_Members.TryGetValue(ownerType, out EntityPropertyMember[] members))
                {
                    members = BuildMembers(ownerType, propertyScope);
                    s_Members.Add(ownerType, members);
                }

                return members;
            }
        }

        private static EntityPropertyMember[] BuildMembers(Type ownerType, string propertyScope)
        {
            List<EntityPropertyMember> members = new List<EntityPropertyMember>();
            Dictionary<string, PropertyInfo> propertiesByName = GetEntityPropertiesByName(ownerType);
            Type currentType = ownerType;
            Type stopType = typeof(Entity).IsAssignableFrom(ownerType) ? typeof(Entity) : typeof(EntityComponent);
            while (currentType != null && stopType.IsAssignableFrom(currentType))
            {
                FieldInfo[] fields = currentType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    EntityPropertyAttribute attribute = field.GetCustomAttribute<EntityPropertyAttribute>(false);
                    if (attribute == null)
                    {
                        continue;
                    }

                    string propertyName = GetPropertyName(field);
                    if (propertiesByName.TryGetValue(propertyName, out PropertyInfo property)
                        && property.PropertyType == field.FieldType
                        && property.CanRead
                        && property.CanWrite)
                    {
                        members.Add(new EntityPropertyMember(propertyScope, propertyName, property.PropertyType, attribute, property, field));
                        continue;
                    }

                    members.Add(new EntityPropertyMember(propertyScope, propertyName, field.FieldType, attribute, null, field));
                }

                currentType = currentType.BaseType;
            }

            members.Sort(CompareMember);
            return members.ToArray();
        }

        private static Dictionary<string, PropertyInfo> GetEntityPropertiesByName(Type entityType)
        {
            PropertyInfo[] properties = entityType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Dictionary<string, PropertyInfo> propertiesByName = new Dictionary<string, PropertyInfo>(properties.Length, StringComparer.Ordinal);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                propertiesByName[property.Name] = property;
            }

            return propertiesByName;
        }

        private static string GetPropertyName(FieldInfo field)
        {
            const string Prefix = "__";
            return field.Name.StartsWith(Prefix, StringComparison.Ordinal)
                ? field.Name.Substring(Prefix.Length)
                : field.Name;
        }

        private static string GetComponentPropertyScope(Type componentType)
        {
            string typeName = componentType.Name;
            const string Suffix = "Component";
            return typeName.EndsWith(Suffix, StringComparison.Ordinal)
                ? typeName.Substring(0, typeName.Length - Suffix.Length)
                : typeName;
        }

        private static int CompareAccessor(EntityPropertyAccessor left, EntityPropertyAccessor right)
        {
            return string.Compare(left.PropertyKey, right.PropertyKey, StringComparison.Ordinal);
        }

        private static int CompareMember(EntityPropertyMember left, EntityPropertyMember right)
        {
            return string.Compare(left.PropertyKey, right.PropertyKey, StringComparison.Ordinal);
        }

        private static void EnsureUniquePropertyIds(Type entityType, List<EntityPropertyAccessor> accessors)
        {
            HashSet<ushort> propertyIds = new HashSet<ushort>();
            for (int i = 0; i < accessors.Count; i++)
            {
                if (!propertyIds.Add(accessors[i].PropertyId))
                {
                    throw new InvalidOperationException(entityType.FullName + " has duplicate entity property id: " + accessors[i].PropertyId);
                }
            }
        }

        private static bool CanSerialize(EntityPropertyAttribute attribute, EntitySerializeReason reason)
        {
            switch (reason)
            {
            case EntitySerializeReason.OwnerSync:
                return attribute.HasFlag(EntityPropertyFlag.ClientServer);
            case EntitySerializeReason.Broadcase:
                return attribute.HasFlag(EntityPropertyFlag.AllClients);
            case EntitySerializeReason.Migrate:
                return attribute.HasFlag(EntityPropertyFlag.ServerOnly)
                    || attribute.HasFlag(EntityPropertyFlag.ClientServer)
                    || attribute.HasFlag(EntityPropertyFlag.AllClients);
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
                    throw new InvalidOperationException("Entity string property is too long.");
                }

                return sizeof(ushort) + byteCount;
            }

            if (valueType == typeof(byte[]))
            {
                int byteCount = value == null ? 0 : ((byte[])value).Length;
                if (byteCount > ushort.MaxValue)
                {
                    throw new InvalidOperationException("Entity byte array property is too long.");
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

            throw new NotSupportedException("Unsupported entity property type: " + valueType.FullName);
        }

        private static void WriteValue(byte[] bytes, ref int offset, Type valueType, object value)
        {
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
                WriteUInt32(bytes, ref offset, unchecked((uint)BitConverter.ToInt32(BitConverter.GetBytes((float)value), 0)));
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
                if (!TryReadUInt32(bytes, ref offset, endOffset, out uint floatValue))
                {
                    return false;
                }

                value = BitConverter.ToSingle(BitConverter.GetBytes(floatValue), 0);
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

        private sealed class EntityPropertyAccessor
        {
            public EntityPropertyAccessor(
                string propertyKey,
                string propertyName,
                Type valueType,
                EntityPropertyAttribute attribute,
                PropertyInfo property,
                FieldInfo field,
                object target
            )
            {
                PropertyKey = propertyKey;
                PropertyName = propertyName;
                PropertyId = GetStablePropertyId(propertyKey);
                ValueType = valueType;
                Attribute = attribute;
                Property = property;
                Field = field;
                Target = target;
            }

            public string PropertyKey { get; }
            public string PropertyName { get; }
            public ushort PropertyId { get; }
            public Type ValueType { get; }
            public EntityPropertyAttribute Attribute { get; }
            private PropertyInfo Property { get; }
            private FieldInfo Field { get; }
            private object Target { get; }

            public object GetValue(Entity entity)
            {
                return Property != null ? Property.GetValue(Target, null) : Field.GetValue(Target);
            }

            public void SetValue(Entity entity, object value)
            {
                if (Field != null)
                {
                    Field.SetValue(Target, value);
                    return;
                }

                Property.SetValue(Target, value, null);
            }
        }

        private sealed class EntityPropertyMember
        {
            public EntityPropertyMember(
                string propertyScope,
                string propertyName,
                Type valueType,
                EntityPropertyAttribute attribute,
                PropertyInfo property,
                FieldInfo field
            )
            {
                PropertyScope = propertyScope ?? string.Empty;
                PropertyName = propertyName;
                PropertyKey = string.IsNullOrEmpty(PropertyScope) ? propertyName : PropertyScope + "." + propertyName;
                ValueType = valueType;
                Attribute = attribute;
                Property = property;
                Field = field;
            }

            public string PropertyScope { get; }
            public string PropertyName { get; }
            public string PropertyKey { get; }
            public Type ValueType { get; }
            public EntityPropertyAttribute Attribute { get; }
            private PropertyInfo Property { get; }
            private FieldInfo Field { get; }

            public EntityPropertyAccessor CreateAccessor(object target)
            {
                return new EntityPropertyAccessor(PropertyKey, PropertyName, ValueType, Attribute, Property, Field, target);
            }
        }

        private static ushort GetStablePropertyId(string propertyName)
        {
            const uint Offset = 2166136261u;
            const uint Prime = 16777619u;

            uint hash = Offset;
            for (int i = 0; i < propertyName.Length; i++)
            {
                hash ^= propertyName[i];
                hash *= Prime;
            }

            return (ushort)(((hash >> 16) ^ hash) & 0xffff);
        }
    }
}
