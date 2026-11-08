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

    public sealed class SpatialState
    {
        private readonly ISpatialEntity _owner;

        public SpatialState(ISpatialEntity owner)
        {
            _owner = owner;
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
