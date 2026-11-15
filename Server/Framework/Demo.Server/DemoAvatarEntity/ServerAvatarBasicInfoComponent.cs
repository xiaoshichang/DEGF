using DE.Server.Entities;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace Demo;


public partial class ServerAvatarBasicInfoComponent : ServerEntityComponent
{
    [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.Persistent)]
    private string __HeadIcon = null!;

    [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.Persistent)]
    private int __Score;

    private DemoAvatarEntity _owner = null!;

    /// <summary>初始化基础信息默认属性和所属 Avatar 字段，不处理组件依赖。</summary>
    protected override void PreInitialize()
    {
        base.PreInitialize();
        __HeadIcon = "xxx.png";
        __Score = 1;
        _owner = (DemoAvatarEntity)OwnerEntity;
    }

    /// <summary>依赖清理完成后释放所属 Avatar 字段。</summary>
    protected override void PostUnInitialize()
    {
        _owner = null!;
        base.PostUnInitialize();
    }

    [ServerRpc]
    public void SetHeadIcon(string headIcon)
    {
        HeadIcon = headIcon ?? string.Empty;
        _owner.CallClient("NotifyHeadIconChanged", HeadIcon);
    }
}
