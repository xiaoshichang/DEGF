using DE.Server.Entities;
using DE.Share.Entities;

namespace Demo;

public partial class DemoAvatarEntity : ServerAvatarEntity
{
    public DemoAvatarEntity()
    {
        BasicInfo = AddComponent(new BasicInfoComponent());
    }

    public BasicInfoComponent BasicInfo { get; }

    [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.Persistent)]
    private string __HeadIcon = null!;

    [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.Persistent)]
    private int __Score;

    protected override void PreInitialize()
    {
        base.PreInitialize();
        HeadIcon = "xxx.png";
        Score = 1;
    }
}
