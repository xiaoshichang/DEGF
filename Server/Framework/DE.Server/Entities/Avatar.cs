namespace DE.Server.Entities
{
    public class AvatarEntity : ServerEntity
    {
        public AvatarEntity()
        {
        }

        public AvatarEntity(object entityDocument) : base(entityDocument)
        {
        }

        public override bool IsMigratable()
        {
            return true;
        }
    }
}
