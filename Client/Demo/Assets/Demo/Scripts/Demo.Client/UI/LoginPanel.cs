using Assets.Scripts.DE.Client.Core;
using Assets.Scripts.DE.Client.UI;
using System.Collections;
using UnityEngine;

namespace Assets.Scripts.Demo.Client.UI
{
    [Panel("Demo/Asb/UI/Login/LoginPanel.prefab")]
    public class LoginPanel : PanelBase
    {
        protected override void Bind()
        {
            DELogger.Info("Bind");
        }
        protected override void OnShow(object arg)
        {
            DELogger.Info("OnShow");
        }
        protected override void OnHide()
        {
        }
    }
}
