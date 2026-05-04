namespace DE.Share.Entities
{
    public abstract class EntityComponent
    {
        public Entity Entity { get; private set; }

        internal void Attach(Entity entity)
        {
            Entity = entity;
        }
    }
}
