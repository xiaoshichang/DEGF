using Assets.Scripts.DE.Client.Framework;
using DE.Share.Rpc;

namespace DE.Client.Entities
{
    public partial class ClientAvatarEntity : ClientEntity
    {
        public ClientAvatarEntity()
        {
        }

        public ClientAvatarEntity(object entityDocument) : base(entityDocument)
        {
        }

        /// <summary>初始化 Avatar 字段，并构造组件及其默认属性；此阶段不处理组件依赖。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            SpaceCmpt = AddComponent(new ClientAvatarSpaceComponent(), nameof(SpaceCmpt));
            TransformCmpt = AddComponent(new ClientAvatarTransformComponent(), nameof(TransformCmpt));
        }

        public ClientAvatarSpaceComponent SpaceCmpt { get; private set; }
        public ClientAvatarTransformComponent TransformCmpt { get; private set; }

        public override void OnRemovedFromSpace()
        {
            TransformCmpt.OnRemovedFromSpace();
            base.OnRemovedFromSpace();
        }

        public bool CallServer(string methodName, params object[] args)
        {
            uint methodId = RpcMethodId.Compute(methodName, args);
            byte[] argsPayload = RpcBinaryWriter.SerializeArguments(args);
            return AuthSystem.Instance.SendAvatarServerRpc(methodId, argsPayload);
        }
    }
}
