using System;
using DE.Share.Entities;
using DE.Share.Foundation.Mathematics;

namespace DE.Client.Entities
{
    public sealed class ClientSpaceComponent : ClientEntityComponent
    {
        private SpatialState _state;

        protected override void PreInitialize()
        {
            base.PreInitialize();
            _state = new SpatialState((ISpatialEntity)OwnerEntity);
        }

        protected override void UnInitialize()
        {
            _state = null;
            base.UnInitialize();
        }

        public void SetTransform(Vector3 position, Quaternion rotation)
        {
            _state.SetTransform(position, rotation);
        }

        public void SetSpace(Guid spaceId, int spaceNo)
        {
            _state.SetSpace(spaceId, spaceNo);
        }
    }
}
