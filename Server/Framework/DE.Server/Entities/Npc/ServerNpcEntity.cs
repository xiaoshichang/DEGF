namespace DE.Server.Entities
{
    public partial class ServerNpcEntity : ServerEntity
    {
        public ServerNpcEntity()
        {
        }

        public ServerNpcEntity(object entityDocument) : base(entityDocument)
        {
        }

        public override bool IsAllowImmigrate()
        {
            return true;
        }
    }
}
