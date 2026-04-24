using System;
using DE.Server.NativeBridge;

namespace DE.Server.Entities
{
    public abstract class ServerStubEntity : ServerEntity
    {
        private bool _isReady;

        public ServerStubEntity()
        {
        }

        public ServerStubEntity(object entityDocument) : base(entityDocument)
        {
        }

        public override bool IsMigratable()
        {
            return false;
        }

        public abstract void InitStub();

        protected void OnReady()
        {
            if (_isReady)
            {
                return;
            }

            _isReady = true;
            DELogger.Info(nameof(ServerStubEntity), $"{nameof(ServerStubEntity)} ready");
            GameServerRuntimeState.NotifyStubReady(this);
        }
    }
}
