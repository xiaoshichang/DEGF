namespace DE.Server.Entities
{
    public partial class NpcEntity : ServerEntity
    {
        public NpcEntity()
        {
            AOIAgent = AddComponent(new AOIAgentComponent());
        }

        public NpcEntity(object entityDocument) : base(entityDocument)
        {
            AOIAgent = AddComponent(new AOIAgentComponent());
        }

        public AOIAgentComponent AOIAgent { get; }
    
        public override bool IsAllowImmigrate()
        {
            return true;
        }   
        
    }
}
