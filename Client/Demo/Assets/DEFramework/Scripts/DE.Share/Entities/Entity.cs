using System;
using System.Collections.Generic;

namespace DE.Share.Entities
{
    public abstract partial class Entity
    {
        protected Entity()
        {
        }

        protected Entity(object entityDocument)
        {
        }

        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Guid __Guid;

        public IReadOnlyList<EntityComponent> Components => _Components;

        private bool _initializationStarted;
        public bool IsInitialized { get; private set; }

        /// <summary>Runs once, after construction. Restores snapshots after defaults and before component initialization.</summary>
        public void InitializeEntity(
            byte[] propertyData = null,
            EntitySerializeReason reason = EntitySerializeReason.OwnerSync,
            Guid? entityId = null)
        {
            if (_initializationStarted)
            {
                throw new InvalidOperationException("Entity initialization has already started.");
            }
            _initializationStarted = true;
            PreInitialize();
            if (entityId.HasValue)
            {
                Guid = entityId.Value;
            }
            if (propertyData != null)
            {
                EntitySerializer.Deserialize(this, reason, propertyData);
            }
            Initialize();
            foreach (var component in _Components)
            {
                if (!component.IsInitialized)
                {
                    throw new InvalidOperationException("Entity.Initialize must initialize every attached component.");
                }
            }
            PostInitialize();
            IsInitialized = true;
        }

        protected virtual void PreInitialize()
        {
            Guid = Guid.NewGuid();
        }

        protected virtual void Initialize()
        {
            foreach (var component in _Components)
            {
                component.InitializeComponent();
            }
        }

        protected virtual void PostInitialize()
        {
        }

        protected TComponent AddComponent<TComponent>(TComponent component) where TComponent : EntityComponent
        {
            if (_initializationStarted)
            {
                throw new InvalidOperationException("Components must be constructed before entity initialization starts.");
            }
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
