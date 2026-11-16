using DE.Share.Entities;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public partial class ServerAvatarEntity : ServerEntity
    {
        public override bool AutoSyncProperty => true;

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
            ImmigateCmpt = AddComponent(new ServerAvatarImmigateComponent());
            TransformCmpt = AddComponent(new ServerAvatarTransformComponent());
            SpaceCmpt = AddComponent(new ServerAvatarSpaceComponent());
            SessionCmpt = AddComponent(new ServerAvatarSessionComponent());
            AOIAgentCmpt = AddComponent(new ServerAvatarAOIAgentComponent());
        }

        [EntityComponent]
        public ServerAvatarSessionComponent SessionCmpt { get; private set; }
        [EntityComponent]
        public ServerAvatarImmigateComponent ImmigateCmpt { get; private set; }
        [EntityComponent]
        public ServerAvatarTransformComponent TransformCmpt { get; private set; }
        [EntityComponent]
        public ServerAvatarSpaceComponent SpaceCmpt { get; private set; }
        [EntityComponent]
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
