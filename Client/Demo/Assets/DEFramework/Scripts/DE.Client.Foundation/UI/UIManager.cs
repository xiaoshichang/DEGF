using System;
using Assets.Scripts.DE.Client.Core;
using UnityEngine;

namespace Assets.Scripts.DE.Client.UI
{
    public partial class UIManager
    {
        public static UIManager Instance;

        public UIManager()
        {
        }

        public void Init()
        {
            _InitUIFramework();
            DELogger.Info("UIManager", "UIManager initialized.");
        }

        public void UnInit()
        {
            _UninitUIFramework();
            DELogger.Info("UIManager", "UIManager uninitialized.");
        }

        public GameObject RootNode => _RootNode;

        public GameObject DebugInfoLayerNode => _DebugInfoLayerNode;

        public GameObject ScreenTransferLayerNode => _ScreenTransferLayerNode;

        public GameObject NotificationLayerNode => _NotificationLayerNode;

        public GameObject DialogLayerNode => _DialogLayerNode;

        public GameObject PanelLayerNode => _PanelLayerNode;

        private GameObject _RootNode;
        private GameObject _DebugInfoLayerNode;
        private GameObject _ScreenTransferLayerNode;
        private GameObject _NotificationLayerNode;
        private GameObject _DialogLayerNode;
        private GameObject _PanelLayerNode;
        private GameObject _EventSystemNode;
    }
}
