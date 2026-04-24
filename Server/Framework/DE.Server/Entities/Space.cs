namespace DE.Server.Entities
{
    public class SpaceEntity : ServerEntity
    {
        public SpaceEntity()
        {
        }

        public SpaceEntity(object entityDocument) : base(entityDocument)
        {
        }
    
        public override bool IsMigratable()
        {
            return false;
        }  
    }
}
