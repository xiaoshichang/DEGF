
using UnityEngine;

namespace Assets.Scripts.DE.Client.UI
{
    public partial class UIManager
    {
        public UIManager()
        {
        }

        public void Init()
        {
            _InitUIFramework();
        }

        public void UnInit()
        {
            _UninitUIFramework();
        }
       
    }
}
