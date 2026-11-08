namespace DE.Server.Entities
{
    public partial class ServerNpcEntity : ServerEntity
    {
        public ServerNpcEntity()
        {
            AOIAgent = AddComponent(new AOIAgentComponent());
        }

        public ServerNpcEntity(object entityDocument) : base(entityDocument)
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
