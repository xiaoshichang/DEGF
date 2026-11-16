using System;
using System.Collections.Generic;

namespace DE.Client.Entities
{
    /// <summary>合并继承链的生成属性元数据，以稳定的属性名哈希查找，不依赖声明顺序或基类属性数量。</summary>
    public sealed class EntityClassMetadata
    {
        private readonly Dictionary<ulong, EntityPropertyMetadata> _propertiesById = new Dictionary<ulong, EntityPropertyMetadata>();

        public EntityClassMetadata(Type ownerType, EntityClassMetadata baseMetadata, params EntityPropertyMetadata[] declaredProperties)
        {
            OwnerType = ownerType ?? throw new ArgumentNullException(nameof(ownerType));
            BaseMetadata = baseMetadata;
            if (declaredProperties == null)
            {
                throw new ArgumentNullException(nameof(declaredProperties));
            }

            var properties = new List<EntityPropertyMetadata>();
            if (baseMetadata != null)
            {
                foreach (var property in baseMetadata.Properties)
                {
                    AddProperty(properties, property);
                }
            }
            foreach (var property in declaredProperties)
            {
                AddProperty(properties, property);
            }
            properties.Sort((left, right) => left.Id.CompareTo(right.Id));
            Properties = properties.AsReadOnly();
        }

        public Type OwnerType { get; }
        public EntityClassMetadata BaseMetadata { get; }
        public IReadOnlyList<EntityPropertyMetadata> Properties { get; }
        public int PropertyCount => Properties.Count;

        public bool TryGetProperty(ulong id, out EntityPropertyMetadata property)
        {
            return _propertiesById.TryGetValue(id, out property);
        }

        private void AddProperty(List<EntityPropertyMetadata> properties, EntityPropertyMetadata property)
        {
            if (property == null)
            {
                throw new ArgumentException("Property metadata must not be null.", nameof(property));
            }
            if (_propertiesById.TryGetValue(property.Id, out var previous))
            {
                throw new InvalidOperationException(
                    $"Property hash collision in {OwnerType.FullName}: '{previous.Name}' and '{property.Name}' (0x{property.Id:X16}).");
            }
            _propertiesById.Add(property.Id, property);
            properties.Add(property);
        }
    }
}
