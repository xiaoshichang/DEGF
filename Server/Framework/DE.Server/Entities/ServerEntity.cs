using System.Collections.Generic;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public abstract partial class ServerEntity : Entity
    {
        private readonly EntityComponentCollection<ServerEntityComponent> _components = new();

        protected ServerEntity()
        {
        }

        protected ServerEntity(object entityDocument) : base(entityDocument)
        {
        }

        public EntityMailBox MailBox { get; private set; }
        public IReadOnlyList<ServerEntityComponent> Components => _components.Items;

        protected TComponent AddComponent<TComponent>(TComponent component) where TComponent : ServerEntityComponent
        {
            return _components.Add(this, component);
        }

        public bool TryGetComponent<TComponent>(out TComponent component) where TComponent : ServerEntityComponent
        {
            return _components.TryGet(out component);
        }

        public TComponent GetComponent<TComponent>() where TComponent : ServerEntityComponent
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

        public abstract bool IsAllowImmigrate();

        internal void AttachToGameServer(string bindingGame)
        {
            MailBox = new EntityMailBox(Guid, bindingGame);
        }
    }
}
