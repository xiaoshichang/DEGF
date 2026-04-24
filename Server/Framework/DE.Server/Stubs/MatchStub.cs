using DE.Server.Entities;

namespace DE.Server.Stubs
{
    public class MatchStub : ServerStubEntity
    {
        public override void InitStub()
        {
            NativeBridge.DELogger.Info(nameof(MatchStub), "InitStub");
            OnReady();
        }
    }
}
