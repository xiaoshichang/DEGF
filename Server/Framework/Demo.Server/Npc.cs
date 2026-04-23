using DE.Server.Entities;

namespace Demo;

public sealed class NpcEntity : ServerEntity
{
    public NpcEntity()
    {
    }

    public NpcEntity(object entityDocument) : base(entityDocument)
    {
    }
    
    public override bool IsMigratable()
    {
        return true;
    }   
        
}
