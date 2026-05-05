using DE.Server.Entities;
using DE.Server.NativeBridge;
using DE.Share.Rpc;

namespace DE.Server.Stubs
{
    public partial class LoginStub : ServerStubEntity
    {
        public override void InitStub()
        {
            NativeBridge.DELogger.Info(nameof(LoginStub), "InitStub");
            OnReady();
        }

        [ServerRpc]
        public void OnAvatarLogin(EntityProxy proxy)
        {
            DELogger.Info(nameof(LoginStub), $"Avatar registered to LoginStub, avatarId={proxy.EntityId}, bindingGate={proxy.BindingGate}.");
            AvatarRpcCaller.CallAvatarProxy(proxy, "OnAvatarLoginFinish");
        }
    }
}
