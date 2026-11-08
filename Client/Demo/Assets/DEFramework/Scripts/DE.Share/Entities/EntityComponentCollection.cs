using System;
using System.Collections.Generic;

namespace DE.Share.Entities
{
    public sealed class EntityComponentCollection<TComponent> where TComponent : EntityComponent
    {
        private readonly List<TComponent> _components = new List<TComponent>();
        private readonly Dictionary<Type, TComponent> _componentsByType = new Dictionary<Type, TComponent>();

        public IReadOnlyList<TComponent> Items => _components;

        public T Add<T>(Entity owner, T component) where T : TComponent
        {
            if (owner.LifecycleState != EntityLifecycleState.Created)
            {
                throw new InvalidOperationException("Components must be constructed before entity initialization starts.");
            }
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }
            var type = component.GetType();
            if (_componentsByType.ContainsKey(type))
            {
                throw new InvalidOperationException("Entity component already exists: " + type.FullName);
            }
            component.Attach(owner);
            _components.Add(component);
            _componentsByType.Add(type, component);
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

        public void Initialize()
        {
            foreach (var component in _components)
            {
                component.InitializeComponent();
            }
        }

        public void UnInitialize()
        {
            List<Exception> errors = null;
            for (int index = _components.Count - 1; index >= 0; index--)
            {
                try
                {
                    _components[index].UnInitializeComponent();
                }
                catch (Exception exception)
                {
                    errors ??= new List<Exception>();
                    errors.Add(exception);
                }
            }
            if (errors != null)
            {
                throw new AggregateException("Entity component cleanup failed.", errors);
            }
        }
    }
}
