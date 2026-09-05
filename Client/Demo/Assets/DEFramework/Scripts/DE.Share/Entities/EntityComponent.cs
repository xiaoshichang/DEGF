namespace DE.Share.Entities
{
    public abstract class EntityComponent
    {
        public Entity OwnerEntity { get; private set; }

        public virtual void Attach(Entity entity)
        {
            OwnerEntity = entity;
        }
    }
}
