using DE.Server.Entities;

namespace Demo;
public partial class DemoAvatarEntity : ServerAvatarEntity
{
    /// <summary>构造基础信息组件并初始化其默认属性与字段。</summary>
    protected override void PreInitialize()
    {
        base.PreInitialize();
        BasicInfo = AddComponent(new ServerAvatarBasicInfoComponent());
    }

    public ServerAvatarBasicInfoComponent BasicInfo { get; private set; } = null!;


}
