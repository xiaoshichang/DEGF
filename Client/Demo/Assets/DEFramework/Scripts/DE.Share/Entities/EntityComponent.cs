using System;

namespace DE.Share.Entities
{
    public interface IEntityComponentProvider
    {
        System.Collections.Generic.IEnumerable<EntityComponent> PropertyComponents { get; }
    }

    public abstract class EntityComponent
    {
        public virtual string PropertyScope
        {
            get
            {
                string name = GetType().Name;
                return name.StartsWith("Server", StringComparison.Ordinal) || name.StartsWith("Client", StringComparison.Ordinal)
                    ? name.Substring(6) : name;
            }
        }

        public Entity OwnerEntity { get; private set; }
        internal void Attach(Entity entity)
        {
            OwnerEntity = entity;
        }

        /// <summary>由实体 PreInitialize 中的 AddComponent 调用，挂载后立即准备组件默认属性与字段。</summary>
        internal void PreInitializeComponent()
        {
            PreInitialize();
        }

        /// <summary>由实体 Initialize 调用，全部组件已构造且属性已恢复，此时处理组件依赖。</summary>
        internal void InitializeComponent()
        {
            Initialize();
        }

        /// <summary>由实体 PostInitialize 调用，全部组件依赖初始化完成后处理收尾逻辑。</summary>
        internal void PostInitializeComponent()
        {
            PostInitialize();
        }

        /// <summary>由实体 PreUnInitialize 逆序调用，撤销收尾逻辑并停止活动。</summary>
        internal void PreUnInitializeComponent()
        {
            PreUnInitialize();
        }

        /// <summary>由实体 UnInitialize 逆序调用，解除组件依赖；属性、字段及 OwnerEntity 暂时保留。</summary>
        internal void UnInitializeComponent()
        {
            UnInitialize();
        }

        /// <summary>由实体 PostUnInitialize 逆序调用，清理字段，正常返回后解除挂载。</summary>
        internal void PostUnInitializeComponent()
        {
            PostUnInitialize();
            OwnerEntity = null;
        }

        /// <summary>在实体 PreInitialize 中初始化组件的属性与字段，不使用其他组件的依赖；默认值随后可由快照覆盖。</summary>
        protected virtual void PreInitialize()
        {
        }

        /// <summary>全部组件已准备且快照已恢复，建立有依赖关系的组件资源；按装配顺序执行，重写时调用 base。</summary>
        protected virtual void Initialize()
        {
        }

        /// <summary>全部组件的依赖初始化完成后处理本组件的收尾逻辑，重写时调用 base。</summary>
        protected virtual void PostInitialize()
        {
        }

        /// <summary>反向撤销收尾逻辑，停止活动、任务和订阅；需容忍部分初始化并调用 base。</summary>
        protected virtual void PreUnInitialize()
        {
        }

        /// <summary>反向解除 Initialize 中建立的组件依赖并释放相关资源，重写时调用 base。</summary>
        protected virtual void UnInitialize()
        {
        }

        /// <summary>反向清理 PreInitialize 准备的属性、字段，正常返回后由入口断开 OwnerEntity。</summary>
        protected virtual void PostUnInitialize()
        {
        }
    }
}
