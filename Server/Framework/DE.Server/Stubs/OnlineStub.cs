using DE.Server.Entities;

namespace DE.Server.Stubs
{
    public class OnlineStub : ServerStubEntity
    {
        public override void InitStub()
        {
            NativeBridge.DELogger.Info(nameof(OnlineStub), "InitStub");
            OnReady();
        }

        protected override void OnStubReady()
        {
            NativeBridge.DELogger.Info(nameof(OnlineStub), "OnStubReady");
        }
    }
}
