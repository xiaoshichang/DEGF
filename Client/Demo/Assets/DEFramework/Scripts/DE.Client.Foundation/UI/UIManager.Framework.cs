using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Assets.Scripts.DE.Client.UI
{
    public partial class UIManager
    {
        private const int PanelLayerSortingOrder = 100;
        private const int DialogLayerSortingOrder = 200;
        private const int NotificationLayerSortingOrder = 300;
        private const int ScreenTransferLayerSortingOrder = 400;
        private const int DebugInfoLayerSortingOrder = 500;

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

            _EnsureEventSystem();
            _StretchToFullScreen(_RootNode.GetComponent<RectTransform>());

            _PanelLayerNode = _CreateLayerNode("PanelLayer", _RootNode.transform, PanelLayerSortingOrder);
            _DialogLayerNode = _CreateLayerNode("DialogLayer", _RootNode.transform, DialogLayerSortingOrder);
            _NotificationLayerNode = _CreateLayerNode("NotificationLayer", _RootNode.transform, NotificationLayerSortingOrder);
            _ScreenTransferLayerNode = _CreateLayerNode("ScreenTransferLayer", _RootNode.transform, ScreenTransferLayerSortingOrder);
            _DebugInfoLayerNode = _CreateLayerNode("DebugInfoLayer", _RootNode.transform, DebugInfoLayerSortingOrder);

            _InitDebugInfoLayer();
            _InitScreenTransferLayer();
            _InitPanelLayer();
        }

        private void _UninitUIFramework()
        {
            if (_GMPanelController != null)
            {
                _GMPanelController.GMCommandDispatched -= _HandleGMCommandDispatched;
            }

            if (_ScreenTransferLayerController != null)
            {
                _ScreenTransferLayerController.ScreenTransferEntered -= _HandleScreenTransferEntered;
                _ScreenTransferLayerController.ScreenTransferExited -= _HandleScreenTransferExited;
            }

            _UninitPanelLayer();

            _PerformanceDataPanelController = null;
            _GMPanelController = null;
            _ScreenTransferLayerController = null;
            _DebugInfoLayerNode = null;
            _ScreenTransferLayerNode = null;
            _NotificationLayerNode = null;
            _DialogLayerNode = null;
            _PanelLayerNode = null;

            if (_EventSystemNode != null)
            {
                Object.Destroy(_EventSystemNode);
                _EventSystemNode = null;
            }

            if (_RootNode == null)
            {
                return;
            }

            Object.Destroy(_RootNode);
            _RootNode = null;
        }

        private GameObject _CreateLayerNode(string nodeName, Transform parentNode, int sortingOrder)
        {
            var layerNode = new GameObject(nodeName, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup));
            layerNode.transform.SetParent(parentNode, false);

            var rectTransform = layerNode.GetComponent<RectTransform>();
            _StretchToFullScreen(rectTransform);

            var canvas = layerNode.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            canvas.vertexColorAlwaysGammaSpace = true;

            return layerNode;
        }

        private void _EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            _EventSystemNode = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Object.DontDestroyOnLoad(_EventSystemNode);
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
