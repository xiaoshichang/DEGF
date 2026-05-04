using DE.Server.Entities;
using DE.Share.Entities;

namespace Demo;

public partial class DemoAvatarEntity : AvatarEntity
{
    public DemoAvatarEntity()
    {
        BasicInfo = AddComponent(new BasicInfoComponent());
    }

    public BasicInfoComponent BasicInfo { get; }
}

