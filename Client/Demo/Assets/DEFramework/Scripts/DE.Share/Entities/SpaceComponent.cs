using System;
using DE.Share.Foundation.Mathematics;

namespace DE.Share.Entities
{
    public interface ISpatialEntity
    {
        Guid SpaceId { get; set; }
        int SpaceNo { get; set; }
        Vector3 Position { get; set; }
        Quaternion Rotation { get; set; }
    }

    public sealed class SpaceComponent : EntityComponent
    {
        private ISpatialEntity _owner;

        protected override void PreInitialize()
        {
            base.PreInitialize();
            _owner = OwnerEntity as ISpatialEntity;
            if (_owner == null)
            {
                throw new InvalidOperationException("SpaceComponent requires an entity implementing ISpatialEntity.");
            }
        }

        protected override void UnInitialize()
        {
            _owner = null;
            base.UnInitialize();
        }

        public void SetTransform(Vector3 position, Quaternion rotation)
        {
            _owner.Position = position;
            _owner.Rotation = rotation;
        }

        public void SetSpace(Guid spaceId, int spaceNo)
        {
            _owner.SpaceId = spaceId;
            _owner.SpaceNo = spaceNo;
        }
    }
}
