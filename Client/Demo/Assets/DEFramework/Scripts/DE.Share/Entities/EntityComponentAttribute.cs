using System;

namespace DE.Share.Entities
{
    /// <summary>声明实体属性树上的组件持有属性；属性名是结构节点的协议名称。</summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class EntityComponentAttribute : Attribute
    {
    }
}
