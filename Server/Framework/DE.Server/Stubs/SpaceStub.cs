using DE.Server.Entities;

namespace DE.Server.Stubs
{
    public class SpaceStub : ServerStubEntity
    {
        public override void InitStub()
        {
            NativeBridge.DELogger.Info(nameof(SpaceStub), "InitStub");
            OnReady();
        }

        protected override void OnStubReady()
        {
            NativeBridge.DELogger.Info(nameof(SpaceStub), "OnStubReady");
        }
    }
}
