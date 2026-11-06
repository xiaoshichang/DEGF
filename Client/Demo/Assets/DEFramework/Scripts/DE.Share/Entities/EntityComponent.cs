using System;

namespace DE.Share.Entities
{
    public abstract class EntityComponent
    {
        public Entity OwnerEntity { get; private set; }
        private bool _initializationStarted;
        public bool IsInitialized { get; private set; }

        internal void Attach(Entity entity)
        {
            if (OwnerEntity != null)
            {
                throw new InvalidOperationException("Component is already attached to an entity.");
            }
            OwnerEntity = entity;
        }

        internal void InitializeComponent()
        {
            if (_initializationStarted || OwnerEntity == null)
            {
                throw new InvalidOperationException("Component must be attached and initialized exactly once.");
            }
            _initializationStarted = true;
            PreInitialize();
            Initialize();
            PostInitialize();
            IsInitialized = true;
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
    }
}
