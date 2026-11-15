using System.Collections.Generic;
using DE.Share.Entities;

namespace DE.Client.Entities
{
    public abstract class ClientEntity : Entity, IEntityComponentProvider
    {
        private readonly EntityComponentCollection<ClientEntityComponent> _components = new EntityComponentCollection<ClientEntityComponent>();

        protected ClientEntity()
        {
        }

        protected ClientEntity(object entityDocument)
        {
        }

        public IEnumerable<EntityComponent> PropertyComponents => _components.Items;

        public IReadOnlyList<ClientEntityComponent> Components => _components.Items;

        protected TComponent AddComponent<TComponent>(TComponent component) where TComponent : ClientEntityComponent
        {
            return _components.Add(this, component);
        }

        /// <summary>获取指定类型的组件；未装配该组件时返回 null。</summary>
        public TComponent GetComponent<TComponent>() where TComponent : ClientEntityComponent
        {
            _components.TryGet(out TComponent component);
            return component;
        }

        /// <summary>全部组件已构造并准备默认值，按装配顺序处理组件依赖初始化。</summary>
        protected override void Initialize()
        {
            base.Initialize();
            _components.Initialize();
        }

        /// <summary>全部组件依赖初始化后，按装配顺序执行组件收尾逻辑。</summary>
        protected override void PostInitialize()
        {
            base.PostInitialize();
            _components.PostInitialize();
        }

        /// <summary>逆序撤销组件收尾逻辑并停止活动，保留组件依赖供后续清理。</summary>
        protected override void PreUnInitialize()
        {
            _components.PreUnInitialize();
            base.PreUnInitialize();
        }

        /// <summary>逆序解除组件依赖并释放相关资源。</summary>
        protected override void UnInitialize()
        {
            _components.UnInitialize();
            base.UnInitialize();
        }

        /// <summary>逆序清理组件字段并解除挂载，清空组件集合。</summary>
        protected override void PostUnInitialize()
        {
            _components.PostUnInitialize();
            base.PostUnInitialize();
        }

        public virtual void OnRemovedFromSpace()
        {
        }
    }
}
