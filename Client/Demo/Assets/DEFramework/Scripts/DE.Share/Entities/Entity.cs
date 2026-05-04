using System;
using System.Collections.Generic;

namespace DE.Share.Entities
{
    public abstract partial class Entity
    {
        protected Entity()
        {
            Guid = Guid.NewGuid();
        }

        protected Entity(object entityDocument)
        {
        }

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.Persistent)]
        private Guid __Guid;

        public IReadOnlyList<EntityComponent> Components => _Components;

        protected TComponent AddComponent<TComponent>(TComponent component) where TComponent : EntityComponent
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            Type componentType = component.GetType();
            if (_ComponentsByType.ContainsKey(componentType))
            {
                throw new InvalidOperationException("Entity component already exists: " + componentType.FullName);
            }

            component.Attach(this);
            _Components.Add(component);
            _ComponentsByType.Add(componentType, component);
            return component;
        }

        public bool TryGetComponent<TComponent>(out TComponent component) where TComponent : EntityComponent
        {
            if (_ComponentsByType.TryGetValue(typeof(TComponent), out EntityComponent foundComponent))
            {
                component = (TComponent)foundComponent;
                return true;
            }

            component = null;
            return false;
        }

        public TComponent GetComponent<TComponent>() where TComponent : EntityComponent
        {
            TryGetComponent(out TComponent component);
            return component;
        }

        private readonly List<EntityComponent> _Components = new List<EntityComponent>();
        private readonly Dictionary<Type, EntityComponent> _ComponentsByType = new Dictionary<Type, EntityComponent>();
    }
}
