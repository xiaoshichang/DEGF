using DE.Server.Entities;

namespace DE.Server.Stubs
{
    public partial class RoomStub : ServerStubEntity
    {
        public override void InitStub()
        {
            NativeBridge.DELogger.Info(nameof(RoomStub), "InitStub");
            OnReady();
        }
    }
}
