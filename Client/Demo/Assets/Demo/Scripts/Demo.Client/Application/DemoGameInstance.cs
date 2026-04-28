

using Assets.Scripts.DE.Client.Core;
using Assets.Scripts.DE.Client.Framework;
using Assets.Scripts.DE.Client.UI;
using Assets.Scripts.Demo.Client.UI;

namespace Demo.Client.Application
{
    public class DemoGameInstance : GameInstance
    {       
        public override void Init()
        {
            base.Init();
            DELogger.Info("DemoGameInstance initialized");
            UIManager.Instance.PushPanel<LoginPanel>();
        }
    
        public override void Update()
        {
            base.Update();
        }
    
        public override void UnInit()
        {
            base.UnInit();
            DELogger.Info("DemoGameInstance uninitialized");
        }
    }
}