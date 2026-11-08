using System;
using DE.Server.Stubs;
using DE.Share.Data;
using DE.Share.DataFramework;
using DE.Share.Entities;

namespace DE.Server.Entities
{
    public struct SpaceInitializer
    {
        public int SpaceNo;
        public AOISpaceComponent AOISpaceComponent;
    }
    
    public partial class ServerSpaceEntity : ServerEntity
    {
        public ServerSpaceEntity(SpaceInitializer initializer)
        {
            _initializer = initializer;
            AOISpace = AddComponent(initializer.AOISpaceComponent ?? new AOISpaceComponent());
        }

        public AOISpaceComponent AOISpace { get; }

        protected override void PreInitialize()
        {
            base.PreInitialize();
            SpaceNo = _initializer.SpaceNo;
            _dataRow = DataRuntime.GetTable<SpaceDataTable>().GetRow(DataTableKey.FromInt32(SpaceNo));
        }

        internal void CompleteCreation()
        {
            StartStatistics();
            ServerStubCaller.Call(nameof(SpaceStub), nameof(SpaceStub.ReportSpaceCreated), MailBox, SpaceNo);
            ReportStatistics();
        }

        protected override void UnInitialize()
        {
            StopStatistics();
            foreach (var avatar in _avatars.Values)
            {
                avatar.CurrentSpace = null;
            }
            _avatars.Clear();
            _dataRow = null;
            base.UnInitialize();
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
