using System;
using System.Collections.Generic;

namespace DE.Server.Entities
{
    public sealed class ServerEntityComponentCollection<TComponent> where TComponent : ServerEntityComponent
    {
        private readonly List<TComponent> _components = new List<TComponent>();
        private readonly Dictionary<Type, TComponent> _componentsByType = new Dictionary<Type, TComponent>();

        private readonly Dictionary<string, TComponent> _componentsByPropertyName = new Dictionary<string, TComponent>(StringComparer.Ordinal);

        public IReadOnlyList<TComponent> Items => _components;

        public T Add<T>(ServerEntity owner, T component, string ownerPropertyName) where T : TComponent
        {
            if (!owner.IsPreInitializing || owner.LifecycleState != EntityLifecycleState.Initializing)
            {
                throw new InvalidOperationException("Components must be constructed during entity PreInitialize.");
            }
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }
            if (string.IsNullOrEmpty(ownerPropertyName))
            {
                throw new ArgumentException("The owning component property name is required.", nameof(ownerPropertyName));
            }
            if (_componentsByPropertyName.ContainsKey(ownerPropertyName))
            {
                throw new InvalidOperationException("Entity component property already exists: " + ownerPropertyName);
            }
            var type = component.GetType();
            if (_componentsByType.ContainsKey(type))
            {
                throw new InvalidOperationException("ServerEntity component already exists: " + type.FullName);
            }
            component.Attach(owner, ownerPropertyName);
            _components.Add(component);
            _componentsByType.Add(type, component);
            _componentsByPropertyName.Add(ownerPropertyName, component);
            component.PreInitializeComponent();
            return component;
        }

        public bool TryGet<T>(out T component) where T : TComponent
        {
            if (_componentsByType.TryGetValue(typeof(T), out var found))
            {
                component = (T)found;
                return true;
            }
            component = null;
            return false;
        }

        /// <summary>在实体 Initialize 中按装配顺序处理各组件的依赖初始化。</summary>
        public void Initialize()
        {
            foreach (var component in _components)
            {
                component.InitializeComponent();
            }
        }

        /// <summary>在实体 PostInitialize 中按装配顺序处理各组件的收尾逻辑。</summary>
        public void PostInitialize()
        {
            foreach (var component in _components)
            {
                component.PostInitializeComponent();
            }
        }

        /// <summary>在实体 PreUnInitialize 中逆序撤销组件收尾逻辑，单个组件失败不阻断其他组件。</summary>
        public void PreUnInitialize()
        {
            RunCleanup(static component => component.PreUnInitializeComponent());
        }

        /// <summary>在实体 UnInitialize 中逆序解除各组件的依赖关系。</summary>
        public void UnInitialize()
        {
            RunCleanup(static component => component.UnInitializeComponent());
        }

        /// <summary>在实体 PostUnInitialize 中逆序清理组件字段并解除挂载，最终清空组件集合。</summary>
        public void PostUnInitialize()
        {
            try
            {
                RunCleanup(static component => component.PostUnInitializeComponent());
            }
            finally
            {
                _components.Clear();
                _componentsByType.Clear();
                _componentsByPropertyName.Clear();
            }
        }

        private void RunCleanup(Action<TComponent> cleanup)
        {
            List<Exception> errors = null;
            for (int index = _components.Count - 1; index >= 0; index--)
            {
                try
                {
                    cleanup(_components[index]);
                }
                catch (Exception exception)
                {
                    errors ??= new List<Exception>();
                    errors.Add(exception);
                }
            }
            if (errors != null)
            {
                throw new AggregateException("ServerEntity component cleanup failed.", errors);
            }
        }
    }
}
