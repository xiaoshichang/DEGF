using System;
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
        private ScreenTransferLayerController _ScreenTransferLayerController;

        /// <summary>
        /// 表示用于管理性能数据面板的控制器实例。
        /// </summary>
        private PerformanceDataPanelController _PerformanceDataPanelController;

        /// <summary>
        /// GM面板控制器，负责GM面板的显示和GM命令的分发
        /// </summary>
        private GMPanelController _GMPanelController;

        /// <summary>
        /// GM命令被分发时触发，参数为GM命令文本
        /// </summary>
        public event Action<string> GMCommandDispatched;

        /// <summary>
        /// 进入切屏时触发，参数为触发切屏的来源ID。
        /// </summary>
        public event Action<string> ScreenTransferEntered;

        /// <summary>
        /// 离开切屏时触发，参数为触发切屏的来源ID。
        /// </summary>
        public event Action<string> ScreenTransferExited;
    }
}
