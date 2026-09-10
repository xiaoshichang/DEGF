using System;
using DE.Share.Data;

namespace DE.Server.Entities
{
    public struct SpaceInitializer
    {
        public int SpaceID;
    }
    
    public partial class SpaceEntity : ServerEntity
    {
        public SpaceEntity(SpaceInitializer initializer)
        {
            SpaceID = initializer.SpaceID;
            _dataRow =  DataRuntime.GetTable<SpaceDataTable>().GetRow(DataTableKey.FromInt32(SpaceID));
        }

        public SpaceEntity(object entityDocument) : base(entityDocument)
        {
            throw new NotSupportedException();
        }
    
        public override bool IsAllowImmigrate()
        {
            return false;
        }

        public int SpaceID { get; private set; }
        private readonly SpaceDataRow _dataRow;
    }
}
