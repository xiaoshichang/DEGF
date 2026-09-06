using DE.Share.Entities;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public abstract partial class ServerEntity : Entity
    {
        protected ServerEntity()
        {
        }

        protected ServerEntity(object entityDocument) : base(entityDocument)
        {
        }

        public EntityMailBox MailBox { get; private set; }

        public abstract bool IsAllowImmigrate();

        internal void AttachToGameServer(string bindingGame)
        {
            MailBox = new EntityMailBox(Guid, bindingGame);
        }
    }
}
