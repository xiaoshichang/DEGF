using DE.Share.Foundation.Mathematics;

namespace DE.Share.Components
{
    public interface IEntityTransform
    {
        Vector3 Position { get; set; }
        Quaternion Rotation { get; set; }
    }

    public sealed class EntityTransform
    {
        private readonly IEntityTransform _owner;

        public EntityTransform(IEntityTransform owner)
        {
            _owner = owner;
        }

        public void SetTransform(Vector3 position, Quaternion rotation)
        {
            _owner.Position = position;
            _owner.Rotation = rotation;
        }
    }
}
