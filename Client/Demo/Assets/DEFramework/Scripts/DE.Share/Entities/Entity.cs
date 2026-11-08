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
                InitializeComponents();
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
                UnInitializeComponents();
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
            try
            {
                UnInitializeComponents();
            }
            catch (Exception exception)
            {
                errors ??= new List<Exception>();
                errors.Add(exception);
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
        }

        protected virtual void PostInitialize()
        {
        }

        protected virtual void UnInitialize()
        {
        }

        protected virtual void InitializeComponents()
        {
        }

        protected virtual void UnInitializeComponents()
        {
        }
    }
}
