

using Assets.Scripts.DE.Client.Core;
using Assets.Scripts.DE.Client.Framework;

namespace Demo.Client.Application
{
    public class DemoGameInstance : GameInstance
    {       
        public override void Init()
        {
            base.Init();
            DELogger.Info("DemoGameInstance initialized");
        }
    
        public override void Update()
        {
            base.Update();
        }
    
        public override void UnInit()
        {
            base.UnInit();
        }
    }
}