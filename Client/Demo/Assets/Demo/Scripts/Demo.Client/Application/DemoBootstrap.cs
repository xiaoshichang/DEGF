using Assets.Scripts.DE.Client.Framework;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Demo.Client.Application
{
    public static class DemoBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterGameplay()
        {
            ApplicationRoot.RegisterGameplay(
                typeof(DemoGameInstance).Assembly,
                () => new DemoGameInstance());
        }
    }
}
