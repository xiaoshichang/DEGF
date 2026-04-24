namespace DE.Server.Entities
{
    public partial class NpcEntity : ServerEntity
    {
        public NpcEntity()
        {
        }

        public NpcEntity(object entityDocument) : base(entityDocument)
        {
        }
    
        public override bool IsMigratable()
        {
            return true;
        }   
        
    }
}
