using System;
using DE.Server.NativeBridge;
using DE.Share.Data;
using DE.Share.DataFramework;
using DE.Share.Entities;

namespace DE.Server.Entities
{
    public struct SpaceInitializer
    {
        public int SpaceNo;
        public SpaceAoiComponent AoiComponent;
    }
    
    public partial class SpaceEntity : ServerEntity
    {
        public SpaceEntity(SpaceInitializer initializer)
        {
            _initializer = initializer;
            Aoi = AddComponent(initializer.AoiComponent ?? new SpaceAoiComponent());
        }

        protected override void PreInitialize()
        {
            base.PreInitialize();
            SpaceNo = _initializer.SpaceNo;
            _dataRow = DataRuntime.GetTable<SpaceDataTable>().GetRow(DataTableKey.FromInt32(SpaceNo));
        }

        protected internal override void OnRegistered()
        {
            base.OnRegistered();
            _loadTimer = DETimer.AddTimer(5000, ReportLoad, true);
            ReportLoad();
        }

        protected internal override void OnUnregistered()
        {
            if (_loadTimer != 0)
            {
                DETimer.CancelTimer(_loadTimer);
                _loadTimer = 0;
            }
            base.OnUnregistered();
        }

        public SpaceEntity(object entityDocument) : base(entityDocument)
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
        private ulong _loadTimer;
    }
}
