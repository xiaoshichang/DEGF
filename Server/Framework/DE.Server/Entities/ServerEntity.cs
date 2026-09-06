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
            ManagedRuntimeState.RequireCurrentGameServerRuntimeState().RegisterLocalEntity(this);
        }

        protected ServerEntity(object entityDocument) : base(entityDocument)
        {
            ManagedRuntimeState.RequireCurrentGameServerRuntimeState().RegisterLocalEntity(this);
        }

        public EntityMailBox MailBox { get; private set; }

        public abstract bool IsAllowImmigrate();

        internal void AttachToGameServer(string bindingGame)
        {
            MailBox = new EntityMailBox(Guid, bindingGame);
        }
    }
}
