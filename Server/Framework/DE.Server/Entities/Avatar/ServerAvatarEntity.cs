using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public partial class ServerAvatarEntity : ServerEntity
    {
        public ServerAvatarEntity()
        {
        }

        public ServerAvatarEntity(object entityDocument) : base(entityDocument)
        {
        }

        /// <summary>初始化 Avatar 字段，并构造组件及其默认属性；此阶段不处理组件依赖。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            ImmigateCmpt = AddComponent(new ServerAvatarImmigateComponent(), nameof(ImmigateCmpt));
            TransformCmpt = AddComponent(new ServerAvatarTransformComponent(), nameof(TransformCmpt));
            SpaceCmpt = AddComponent(new ServerAvatarSpaceComponent(), nameof(SpaceCmpt));
            SessionCmpt = AddComponent(new ServerAvatarSessionComponent(), nameof(SessionCmpt));
            AOIAgentCmpt = AddComponent(new ServerAvatarAOIAgentComponent(), nameof(AOIAgentCmpt));
        }

        public ServerAvatarSessionComponent SessionCmpt { get; private set; }
        public ServerAvatarImmigateComponent ImmigateCmpt { get; private set; }
        public ServerAvatarTransformComponent TransformCmpt { get; private set; }
        public ServerAvatarSpaceComponent SpaceCmpt { get; private set; }
        public ServerAvatarAOIAgentComponent AOIAgentCmpt { get; private set; }
        
        public override bool IsAllowImmigrate() => true;

        public EntityProxy Proxy { get; private set; }

        internal void AttachToGateServer(string bindingGate)
        {
            Proxy = new EntityProxy(Guid, bindingGate);
        }

        public bool CallClient(string methodName, params object[] args)
        {
            uint methodId = RpcMethodId.Compute(methodName, args);
            byte[] argsPayload = RpcBinaryWriter.SerializeArguments(args);
            return AvatarRpcCaller.CallClient(this, methodId, argsPayload);
        }

    }
}
