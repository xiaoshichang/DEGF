using System.Collections.Generic;
using DE.Share.Entities;

namespace DE.Client.Entities
{
    public abstract class ClientEntity : Entity
    {
        private readonly EntityComponentCollection<ClientEntityComponent> _components = new EntityComponentCollection<ClientEntityComponent>();

        protected ClientEntity()
        {
        }

        protected ClientEntity(object entityDocument) : base(entityDocument)
        {
        }

        public IReadOnlyList<ClientEntityComponent> Components => _components.Items;

        protected TComponent AddComponent<TComponent>(TComponent component) where TComponent : ClientEntityComponent
        {
            return _components.Add(this, component);
        }

        public bool TryGetComponent<TComponent>(out TComponent component) where TComponent : ClientEntityComponent
        {
            return _components.TryGet(out component);
        }

        public TComponent GetComponent<TComponent>() where TComponent : ClientEntityComponent
        {
            TryGetComponent(out TComponent component);
            return component;
        }

        protected sealed override void InitializeComponents()
        {
            _components.Initialize();
        }

        protected sealed override void UnInitializeComponents()
        {
            _components.UnInitialize();
        }

        public virtual void OnRemovedFromSpace()
        {
        }
    }
}
