using System.Collections.Generic;

namespace DE.Client.Entities
{
    /// <summary>SG 生成的属性树节点入口；叶子直接使用强类型代码，继承类串接基类实现。</summary>
    public interface IEntityPropertyNode
    {
        void WriteSnapshot(EntityPropertyWriter writer, EntitySerializationProfile profile);
        bool WriteProperty(ulong propertyId, EntityPropertyWriter writer, EntitySerializationProfile profile);
        bool ReadProperty(ulong propertyId, byte wireTag, ref EntityPropertyReader reader, EntityPropertyReadContext context);
        bool ValidateSnapshot(IReadOnlyCollection<ulong> seen, EntitySerializationProfile profile);
        bool HasSnapshotProperties(EntitySerializationProfile profile);
        void ValidatePropertyTree();
        bool TryGetComponentPropertyHash(IEntityPropertyNode component, out ulong propertyHash);
    }
}
