using DE.Share.Entities;
using System;
using DE.Share.Data;
using DE.Share.DataFramework;

namespace DE.Server.Entities
{
    public struct SpaceInitializer
    {
        public int SpaceNo;
        public ServerSpaceAOIComponent ServerSpaceAOIComponent;
    }
    
    public partial class ServerSpaceEntity : ServerEntity
    {
        public override bool AutoSyncProperty => true;

        public ServerSpaceEntity(SpaceInitializer initializer)
        {
            _initializer = initializer;
        }

        [EntityComponent]
        public ServerSpaceAOIComponent AOISpace { get; private set; }
        [EntityComponent]
        public ServerSpaceAvatarManageComponent AvatarManageCmpt { get; private set; }
        [EntityComponent]
        public ServerSpaceStatisticsComponent StatisticsCmpt { get; private set; }

        /// <summary>设置空间编号和默认字段，构造空间组件并准备组件默认属性。</summary>
        protected override void PreInitialize()
        {
            base.PreInitialize();
            SpaceNo = _initializer.SpaceNo;
            _dataRow = null;
            AOISpace = AddComponent(_initializer.ServerSpaceAOIComponent ?? new ServerSpaceAOIComponent());
            AvatarManageCmpt = AddComponent(new ServerSpaceAvatarManageComponent());
            StatisticsCmpt = AddComponent(new ServerSpaceStatisticsComponent());
        }

        /// <summary>组件和默认字段准备完成、属性恢复后，加载空间配置并初始化组件依赖。</summary>
        protected override void Initialize()
        {
            _dataRow = DataRuntime.GetTable<SpaceDataTable>().GetRow(DataTableKey.FromInt32(SpaceNo));
            base.Initialize();
        }

        /// <summary>组件清理完成后释放空间配置引用。</summary>
        protected override void PostUnInitialize()
        {
            _dataRow = null;
            base.PostUnInitialize();
        }

        public ServerSpaceEntity(object entityDocument) : base(entityDocument)
        {
            throw new NotSupportedException();
        }
    
        public override bool IsAllowImmigrate()
        {
            return false;
        }

        [EntityProperty(EntityPropertyFlag.AllClients)]
        private int __SpaceNo;

        public int MaxPlayers => _dataRow.MaxPlayers;
        private readonly SpaceInitializer _initializer;
        private SpaceDataRow _dataRow;
    }
}
