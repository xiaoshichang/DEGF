using System;
using System.Collections.Generic;
using DE.Server.NativeBridge;
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

        public abstract bool IsMigratable();

        internal void AttachToGameServer(string bindingGame)
        {
            MailBox = new EntityMailBox(Guid, bindingGame);
        }

        protected void RegisterLocalEntity()
        {
            ManagedRuntimeState.RequireCurrentGameServerRuntimeState().RegisterLocalEntity(this);
        }
    }
}
