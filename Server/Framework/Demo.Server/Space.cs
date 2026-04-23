using DE.Server.Entities;

namespace Demo;

public sealed class SpaceEntity : ServerEntity
{
    public SpaceEntity()
    {
    }

    public SpaceEntity(object entityDocument) : base(entityDocument)
    {
    }
    
    public override bool IsMigratable()
    {
        return false;
    }  
}
