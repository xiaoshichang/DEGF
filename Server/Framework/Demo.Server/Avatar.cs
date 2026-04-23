using DE.Server.Entities;

namespace Demo;

public sealed class AvatarEntity : ServerEntity
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
