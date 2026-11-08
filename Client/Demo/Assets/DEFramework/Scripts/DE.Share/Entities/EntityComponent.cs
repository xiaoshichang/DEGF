using System;

namespace DE.Share.Entities
{
    public abstract class EntityComponent
    {
        public Entity OwnerEntity { get; private set; }
        public EntityLifecycleState LifecycleState { get; private set; } = EntityLifecycleState.Created;
        public bool IsInitialized => LifecycleState == EntityLifecycleState.Initialized;

        internal void Attach(Entity entity)
        {
            if (LifecycleState != EntityLifecycleState.Created)
            {
                throw new InvalidOperationException("Cannot attach a component after its lifecycle has started.");
            }
            if (OwnerEntity != null)
            {
                throw new InvalidOperationException("Component is already attached to an entity.");
            }
            OwnerEntity = entity;
        }

        internal void InitializeComponent()
        {
            if (LifecycleState != EntityLifecycleState.Created)
            {
                throw new InvalidOperationException("Component must be initialized exactly once.");
            }
            if (OwnerEntity == null)
            {
                throw new InvalidOperationException("Component must be attached before initialization.");
            }
            LifecycleState = EntityLifecycleState.Initializing;
            PreInitialize();
            Initialize();
            PostInitialize();
            LifecycleState = EntityLifecycleState.Initialized;
        }

        internal void UnInitializeComponent()
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
                OwnerEntity = null;
                LifecycleState = EntityLifecycleState.UnInitialized;
                return;
            }

            LifecycleState = EntityLifecycleState.UnInitializing;
            try
            {
                UnInitialize();
            }
            finally
            {
                OwnerEntity = null;
                LifecycleState = EntityLifecycleState.UnInitialized;
            }
        }

        protected virtual void PreInitialize()
        {
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
    }
}
