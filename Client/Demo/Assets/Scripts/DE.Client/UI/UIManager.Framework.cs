using System.Globalization;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace Assets.Scripts.DE.Client.UI
{
    public partial class UIManager
    {
        public GameObject RootNode => _RootNode;

        public GameObject DebugInfoLayerNode => _DebugInfoLayerNode;

        public GameObject BlackScreenLayerNode => _BlackScreenLayerNode;

        public GameObject NotificationLayerNode => _NotificationLayerNode;

        public GameObject DialogLayerNode => _DialogLayerNode;

        public GameObject PanelLayerNode => _PanelLayerNode;

        private GameObject _RootNode;
        private GameObject _DebugInfoLayerNode;
        private GameObject _BlackScreenLayerNode;
        private GameObject _NotificationLayerNode;
        private GameObject _DialogLayerNode;
        private GameObject _PanelLayerNode;

        private void _InitUIFramework()
        {
            if (_RootNode != null)
            {
                return;
            }

            _RootNode = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Object.DontDestroyOnLoad(_RootNode);

            var canvas = _RootNode.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 0;

            var canvasScaler = _RootNode.GetComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasScaler.matchWidthOrHeight = 0.5f;

            _StretchToFullScreen(_RootNode.GetComponent<RectTransform>());

            _DebugInfoLayerNode = _CreateLayerNode("DebugInfoLayer", _RootNode.transform, 100);
            _BlackScreenLayerNode = _CreateLayerNode("BlackScreenLayer", _RootNode.transform, 200);
            _NotificationLayerNode = _CreateLayerNode("NotificationLayer", _RootNode.transform, 300);
            _DialogLayerNode = _CreateLayerNode("DialogLayer", _RootNode.transform, 400);
            _PanelLayerNode = _CreateLayerNode("PanelLayer", _RootNode.transform, 500);

#if __DEBUG__
            _InitDebugInfoLayer();
#endif
        }


        private void _UninitUIFramework()
        {
            _DebugInfoLayerNode = null;
            _BlackScreenLayerNode = null;
            _NotificationLayerNode = null;
            _DialogLayerNode = null;
            _PanelLayerNode = null;
            _DebugInfoLayerController = null;

            if (_RootNode == null)
            {
                return;
            }

            Object.Destroy(_RootNode);
            _RootNode = null;
        }

        private GameObject _CreateLayerNode(string nodeName, Transform parentNode, int sortingOrder)
        {
            var layerNode = new GameObject(nodeName, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            layerNode.transform.SetParent(parentNode, false);

            var rectTransform = layerNode.GetComponent<RectTransform>();
            _StretchToFullScreen(rectTransform);

            var canvas = layerNode.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            return layerNode;
        }

        

        private void _StretchToFullScreen(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.localScale = Vector3.one;
        }
    }

    
}
