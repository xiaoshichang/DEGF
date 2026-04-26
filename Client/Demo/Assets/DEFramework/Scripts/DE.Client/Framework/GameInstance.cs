

using Assets.Scripts.DE.Client.Core;

namespace Assets.Scripts.DE.Client.Framework
{
    public class GameInstance
    {
        public virtual void Init()
        {

        }

        public virtual void Update()
        {

        }

        public virtual void UnInit()
        {
            
        }


        [GMCommand("Test GM command")]
        public static void TestGM(int a, float b, string c)
        {
            DELogger.Info($"TestGM executed. a: {a}, b: {b}, c: {c}");
        }

        [GMCommand("Test GM command 2")]
        public static void TestGM2(int a, float b)
        {
            DELogger.Info($"TestGM2 executed. a: {a}, b: {b}");
        }
    }
}
