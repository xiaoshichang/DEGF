using Assets.Scripts.DE.Client.Framework;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace Assets.Scripts.Demo.Client.Entities
{
    public partial class DemoAvatarEntity : AvatarEntity
    {
        public DemoAvatarEntity()
        {
            BasicInfo = AddComponent(new BasicInfoComponent());
        }

        public BasicInfoComponent BasicInfo { get; }

        public void RequestSetHeadIcon(string headIcon)
        {
            var writer = new RpcBinaryWriter();
            writer.WriteString(headIcon ?? string.Empty);
            AuthSystem.Instance.SendAvatarServerRpc(RpcMethodId.Compute("SetHeadIcon", "string"), writer.ToArray());
        }

        [ClientRpc]
        public void NotifyHeadIconChanged(string headIcon)
        {
            BasicInfo.HeadIcon = headIcon ?? string.Empty;
        }
    }
}
