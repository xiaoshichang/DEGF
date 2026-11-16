using DE.Share.Entities;
using System;
using System.Collections.Generic;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public enum EntityLifecycleState
    {
        Created,
        Initializing,
        Initialized,
        UnInitializing,
        UnInitialized,
    }

    public abstract partial class ServerEntity
    {
        [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients | EntityPropertyFlag.Persistent)]
        private Guid __Guid;

        private readonly ServerEntityComponentCollection<ServerEntityComponent> _components = new ServerEntityComponentCollection<ServerEntityComponent>();

        protected ServerEntity()
        {
        }

        protected ServerEntity(object entityDocument)
        {
        }

        public EntityMailBox MailBox { get; private set; }

        public IReadOnlyList<ServerEntityComponent> Components => _components.Items;

        /// <summary>装配组件；持有属性的协议标识由 SG 生成的组件引用路由确定。</summary>
        protected TComponent AddComponent<TComponent>(TComponent component) where TComponent : ServerEntityComponent
        {
            return _components.Add(this, component);
        }

        /// <summary>获取指定类型的组件；未装配该组件时返回 null。</summary>
        public TComponent GetComponent<TComponent>() where TComponent : ServerEntityComponent
        {
            _components.TryGet(out TComponent component);
            return component;
        }

        public EntityLifecycleState LifecycleState { get; private set; } = EntityLifecycleState.Created;
        public bool IsInitialized => LifecycleState == EntityLifecycleState.Initialized;

        internal bool IsPreInitializing { get; private set; }

        /// <summary>
        /// 唯一初始化入口：PreInitialize → 恢复身份和属性 → Initialize → PostInitialize。
        /// 只执行一次；阶段失败时中断初始化，调用方通过 UnInitializeEntity 清理部分初始化的资源。
        /// </summary>
        public void InitializeEntity(
            byte[] propertyData = null,
            EntitySerializationProfile profile = EntitySerializationProfile.Owner,
            Guid? entityId = null)
        {
            if (LifecycleState != EntityLifecycleState.Created)
            {
                throw new InvalidOperationException("Entity initialization has already started.");
            }

            LifecycleState = EntityLifecycleState.Initializing;
            IsPreInitializing = true;
            try
            {
                PreInitialize();
            }
            finally
            {
                IsPreInitializing = false;
            }
            if (entityId.HasValue)
            {
                Guid = entityId.Value;
            }
            if (propertyData != null)
            {
                EntitySerializer.RestoreSnapshot(this, profile, propertyData, entityId);
            }
            Initialize();
            PostInitialize();
            LifecycleState = EntityLifecycleState.Initialized;
        }

        /// <summary>
        /// 唯一清理入口：顺序调用 PreUnInitialize、UnInitialize、PostUnInitialize，不捕获异常。
        /// 支持未初始化和部分初始化的实体；重复或重入调用无效，异常直接向外传播并中断后续阶段。
        /// </summary>
        public void UnInitializeEntity()
        {
            if (LifecycleState == EntityLifecycleState.UnInitializing)
            {
                return;
            }
            if (LifecycleState == EntityLifecycleState.UnInitialized)
            {
                return;
            }
            LifecycleState = EntityLifecycleState.UnInitializing;
            PreUnInitialize();
            UnInitialize();
            PostUnInitialize();
            LifecycleState = EntityLifecycleState.UnInitialized;
        }

        /// <summary>
        /// 初始化实体属性和字段，构造组件并准备组件内部的属性与字段；此阶段不处理组件依赖。
        /// 重写时调用 base，默认值允许随后由 InitializeEntity 的 entityId 或属性快照覆盖。
        /// </summary>
        protected virtual void PreInitialize()
        {
            Guid = Guid.NewGuid();
        }

        /// <summary>
        /// 属性和身份恢复后处理有依赖关系的资源及组件初始化；全部组件此时已经构造并完成默认值准备。
        /// 派生类调用 base.Initialize()，并按资源依赖决定自身初始化与 base 调用的先后顺序。
        /// </summary>
        protected virtual void Initialize()
        {
            _components.Initialize();
        }

        /// <summary>
        /// 实体及其组件的依赖初始化完成后处理收尾逻辑；返回后实体才标记为 Initialized。
        /// 此阶段不新增组件，不作为独立的对外可服务通知；重写时调用 base。
        /// </summary>
        protected virtual void PostInitialize()
        {
            _components.PostInitialize();
        }

        /// <summary>
        /// 反向撤销 PostInitialize 的收尾逻辑，停止业务活动、任务和外部订阅，组件及依赖此时仍然有效。
        /// 也可能用于初始化失败或尚未初始化的实体，重写时需容忍未完成的准备并调用 base。
        /// </summary>
        protected virtual void PreUnInitialize()
        {
            _components.PreUnInitialize();
        }

        /// <summary>
        /// 反向解除 Initialize 建立的依赖并释放相关资源；ServerEntity / ClientEntity 在此逆序清理组件依赖。
        /// 派生类先释放依赖组件的自身资源，再调用 base.UnInitialize()，并容忍部分初始化状态。
        /// </summary>
        protected virtual void UnInitialize()
        {
            _components.UnInitialize();
        }

        /// <summary>
        /// 反向清理 PreInitialize 准备的属性、字段和组件引用；不得再使用已经解除的依赖或重启业务。
        /// 仅在前面阶段正常返回后执行；重写时调用 base，返回后实体标记为 UnInitialized。
        /// </summary>
        protected virtual void PostUnInitialize()
        {
            _components.PostUnInitialize();
            ResetAutoSyncProperties();
        }

        public abstract bool IsAllowImmigrate();

        internal void AttachToGameServer(string bindingGame)
        {
            MailBox = new EntityMailBox(Guid, bindingGame);
        }
    }
}
