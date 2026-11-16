using System;

namespace DE.Server.Entities
{
    public abstract partial class ServerEntity
    {
        internal void NotifyPropertiesApplied(EntityPropertyApplyContext context)
        {
            OnPropertiesApplied(context ?? throw new ArgumentNullException(nameof(context)));
        }

        /// <summary>远端属性包完整应用后通知一次；Changes 保持接收顺序，不触发逐属性本地回调。</summary>
        protected virtual void OnPropertiesApplied(EntityPropertyApplyContext context)
        {
        }
    }
}
