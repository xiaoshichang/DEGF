using DE.Server.Entities;

namespace DE.Server.Stubs
{
    public partial class OnlineStub : ServerStubEntity
    {
        public override void InitStub()
        {
            NativeBridge.DELogger.Info(nameof(OnlineStub), "InitStub");
            OnReady();
        }
    }
}
