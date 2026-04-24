using DE.Server.Entities;
using DE.Server.NativeBridge;

namespace DE.Server.Stubs
{
    public class SpaceStub : ServerStubEntity
    {
        public override void InitStub()
        {
            NativeBridge.DELogger.Info(nameof(SpaceStub), "InitStub");
            DETimer.AddTimer(2000, OnReady);
        }
    }
}
