using System;
using System.Collections.Generic;

namespace DE.Share.Entities
{
    public enum EntityLifecycleState
    {
        Created,
        Initializing,
        Initialized,
        UnInitializing,
        UnInitialized,
    }

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

        public EntityLifecycleState LifecycleState { get; private set; } = EntityLifecycleState.Created;
        public bool IsInitialized => LifecycleState == EntityLifecycleState.Initialized;

        /// <summary>Runs once, after construction. Restores snapshots after defaults and before component initialization.</summary>
        public void InitializeEntity(
            byte[] propertyData = null,
            EntitySerializeReason reason = EntitySerializeReason.OwnerSync,
            Guid? entityId = null)
        {
            if (LifecycleState != EntityLifecycleState.Created)
            {
                throw new InvalidOperationException("Entity initialization has already started.");
            }
            LifecycleState = EntityLifecycleState.Initializing;
            try
            {
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
                LifecycleState = EntityLifecycleState.Initialized;
            }
            catch (Exception initializationError)
            {
                try
                {
                    UnInitializeEntity();
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException("Entity initialization and cleanup both failed.", initializationError, cleanupError);
                }
                throw;
            }
        }

        /// <summary>Runs once, then releases attached components in reverse initialization order.</summary>
        public void UnInitializeEntity()
        {
            if (LifecycleState == EntityLifecycleState.UnInitializing)
            {
                return;
            }
            if (LifecycleState == EntityLifecycleState.UnInitialized)
            {
                return;
            }
            if (LifecycleState == EntityLifecycleState.Created)
            {
                LifecycleState = EntityLifecycleState.UnInitialized;
                foreach (var component in _Components)
                {
                    component.UnInitializeComponent();
                }
                return;
            }

            LifecycleState = EntityLifecycleState.UnInitializing;
            List<Exception> errors = null;
            try
            {
                UnInitialize();
            }
            catch (Exception exception)
            {
                errors = new List<Exception> { exception };
            }
            for (int index = _Components.Count - 1; index >= 0; index--)
            {
                try
                {
                    _Components[index].UnInitializeComponent();
                }
                catch (Exception exception)
                {
                    errors ??= new List<Exception>();
                    errors.Add(exception);
                }
            }
            LifecycleState = EntityLifecycleState.UnInitialized;
            if (errors != null)
            {
                throw new AggregateException($"Entity cleanup failed, entityId={Guid}, entityType={GetType().FullName}.", errors);
            }
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

        protected virtual void UnInitialize()
        {
        }

        protected TComponent AddComponent<TComponent>(TComponent component) where TComponent : EntityComponent
        {
            if (LifecycleState != EntityLifecycleState.Created)
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
