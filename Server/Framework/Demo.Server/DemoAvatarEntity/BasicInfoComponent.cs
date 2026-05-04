using DE.Server.Entities;
using DE.Share.Entities;

namespace Demo;


public partial class BasicInfoComponent : EntityComponent
{
    [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.Persistent)]
    private string __HeadIcon = "xxx.png";

    [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.Persistent)]
    private int __Score = 1;
}
