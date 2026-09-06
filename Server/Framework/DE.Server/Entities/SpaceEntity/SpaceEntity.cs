namespace DE.Server.Entities
{
    public partial class SpaceEntity : ServerEntity
    {
        public SpaceEntity()
        {
        }

        public SpaceEntity(object entityDocument) : base(entityDocument)
        {
        }
    
        public override bool IsAllowImmigrate()
        {
            return false;
        }  
    }
}
