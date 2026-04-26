using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Assets.Scripts.DE.Client.Core;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace Assets.Scripts.DE.Client.UI
{
    public class PerformanceDataPanelController : MonoBehaviour
    {
        private const float RefreshInterval = 0.2f;
        private const float BytesPerMegabyte = 1024.0f * 1024.0f;

        public void Bind(Text contentText)
        {
            _ContentText = contentText;
        }

        public void Init()
        {
            _ElapsedTime = 0.0f;
            _FrameCount = 0;
            _RefreshContent(0.0f);
        }

        private void Update()
        {
            _ElapsedTime += Time.unscaledDeltaTime;
            _FrameCount++;

            if (_ElapsedTime < RefreshInterval)
            {
                return;
            }

            var fps = _ElapsedTime > 0.0f
                ? _FrameCount / _ElapsedTime
                : 0.0f;

            _RefreshContent(fps);
            _ElapsedTime = 0.0f;
            _FrameCount = 0;
        }

        private void _RefreshContent(float fps)
        {
            if (_ContentText == null)
            {
                return;
            }

            var totalAllocatedMemoryMB = Profiler.GetTotalAllocatedMemoryLong() / BytesPerMegabyte;
            _ContentText.text = string.Format(
                CultureInfo.InvariantCulture,
                "FPS: {0:F1}\nMemory: {1:F1} MB",
                fps,
                totalAllocatedMemoryMB);
        }

        private Text _ContentText;
        private float _ElapsedTime;
        private int _FrameCount;
    }

    public class GMPanelController : MonoBehaviour
    {
        private const int MaxLogItemCount = 100;
        private static readonly Color OddLogItemBackgroundColor = new Color(1.0f, 1.0f, 1.0f, 0.04f);
        private static readonly Color EvenLogItemBackgroundColor = new Color(1.0f, 1.0f, 1.0f, 0.09f);
        private static readonly Color DebugLogTextColor = new Color(0.68f, 0.94f, 0.72f, 1.0f);
        private static readonly Color InfoLogTextColor = Color.white;
        private static readonly Color WarnLogTextColor = new Color(1.0f, 0.95f, 0.65f, 1.0f);
        private static readonly Color ErrorLogTextColor = new Color(1.0f, 0.72f, 0.72f, 1.0f);
        private readonly Queue<string> _PendingLogMessages = new Queue<string>();
        private readonly object _PendingLogMessagesLock = new object();
        private readonly List<GMLogItemView> _LogItemViews = new List<GMLogItemView>();

        public event Action<string> GMCommandDispatched;

        public void Bind(GameObject panelNode, InputField commandInputField, RectTransform logContentNode, ScrollRect logScrollRect)
        {
            _PanelNode = panelNode;
            _CommandInputField = commandInputField;
            _LogContentNode = logContentNode;
            _LogScrollRect = logScrollRect;
        }

        public void Init()
        {
            if (_PanelNode != null)
            {
                _PanelNode.SetActive(false);
            }

            _AppendLocalLog("[GM] Press ` to toggle GM panel.");
            Application.logMessageReceivedThreaded += _OnLogMessageReceived;
        }

        public void SendCurrentCommand()
        {
            if (_CommandInputField == null)
            {
                return;
            }

            var gmCommand = _CommandInputField.text == null
                ? string.Empty
                : _CommandInputField.text.Trim();
            if (string.IsNullOrEmpty(gmCommand))
            {
                return;
            }

            _AppendLocalLog(">> " + gmCommand);
            GMCommandDispatched?.Invoke(gmCommand);
            if (GMCommandDispatched == null)
            {
                _AppendLocalLog("[GM] No GM command dispatcher registered.");
            }

            _CommandInputField.text = string.Empty;
            _CommandInputField.ActivateInputField();
            _CommandInputField.Select();
        }

        public void OnInputFieldEndEdit(string _)
        {
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                return;
            }

            SendCurrentCommand();
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= _OnLogMessageReceived;
        }

        private void Update()
        {
            _FlushPendingLogs();

            if (_FocusCommandInputNextFrame)
            {
                _FocusCommandInputNextFrame = false;
                _FocusCommandInput();
            }

            if (!Input.GetKeyDown(_ToggleHotKey))
            {
                return;
            }

            _TogglePanelVisibility();
        }

        private void _TogglePanelVisibility()
        {
            if (_PanelNode == null)
            {
                return;
            }

            var shouldShow = !_PanelNode.activeSelf;
            _PanelNode.SetActive(shouldShow);
            if (!shouldShow || _CommandInputField == null)
            {
                return;
            }

            _FocusCommandInputNextFrame = true;
        }

        private void _OnLogMessageReceived(string condition, string stackTrace, LogType logType)
        {
            var logLine = _BuildLogLine(condition, stackTrace, logType);

            lock (_PendingLogMessagesLock)
            {
                _PendingLogMessages.Enqueue(logLine);
            }
        }

        private void _FlushPendingLogs()
        {
            while (true)
            {
                string pendingLogMessage;
                lock (_PendingLogMessagesLock)
                {
                    if (_PendingLogMessages.Count == 0)
                    {
                        break;
                    }

                    pendingLogMessage = _PendingLogMessages.Dequeue();
                }

                _AppendLocalLog(pendingLogMessage);
            }
        }

        private void _AppendLocalLog(string logMessage)
        {
            if (_LogContentNode == null)
            {
                return;
            }

            var normalizedLogMessage = _StripRichTextTags(logMessage);
            var logItemView = _CreateLogItemView(normalizedLogMessage);
            _ApplyNextLogItemBackgroundColor(logItemView);
            _LogItemViews.Add(logItemView);
            if (_LogItemViews.Count > MaxLogItemCount)
            {
                var oldestLogItemView = _LogItemViews[0];
                _LogItemViews.RemoveAt(0);
                if (oldestLogItemView.RootNode != null)
                {
                    Destroy(oldestLogItemView.RootNode);
                }
            }

            Canvas.ForceUpdateCanvases();
            if (_LogScrollRect != null)
            {
                _LogScrollRect.verticalNormalizedPosition = 0.0f;
            }
        }

        private GMLogItemView _CreateLogItemView(string logMessage)
        {
            var logItemNode = new GameObject("LogItem", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            logItemNode.transform.SetParent(_LogContentNode, false);

            var logItemRectTransform = logItemNode.GetComponent<RectTransform>();
            logItemRectTransform.localScale = Vector3.one;

            var logItemLayoutElement = logItemNode.GetComponent<LayoutElement>();
            logItemLayoutElement.minHeight = 28.0f;
            logItemLayoutElement.preferredHeight = -1.0f;
            logItemLayoutElement.flexibleHeight = 0.0f;

            var logItemTextNode = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(ContentSizeFitter));
            logItemTextNode.transform.SetParent(logItemNode.transform, false);

            var logItemTextRectTransform = logItemTextNode.GetComponent<RectTransform>();
            logItemTextRectTransform.anchorMin = Vector2.zero;
            logItemTextRectTransform.anchorMax = Vector2.one;
            logItemTextRectTransform.offsetMin = new Vector2(12.0f, 6.0f);
            logItemTextRectTransform.offsetMax = new Vector2(-12.0f, -6.0f);
            logItemTextRectTransform.localScale = Vector3.one;

            var logItemText = logItemTextNode.GetComponent<Text>();
            logItemText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            logItemText.fontSize = 18;
            logItemText.alignment = TextAnchor.UpperLeft;
            logItemText.horizontalOverflow = HorizontalWrapMode.Wrap;
            logItemText.verticalOverflow = VerticalWrapMode.Overflow;
            logItemText.color = _ResolveLogTextColor(logMessage);
            logItemText.supportRichText = false;
            logItemText.text = logMessage;

            var logItemTextContentSizeFitter = logItemTextNode.GetComponent<ContentSizeFitter>();
            logItemTextContentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            logItemTextContentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return new GMLogItemView
            {
                RootNode = logItemNode,
                BackgroundImage = logItemNode.GetComponent<Image>(),
                ContentText = logItemText,
            };
        }

        private void _ApplyNextLogItemBackgroundColor(GMLogItemView logItemView)
        {
            if (logItemView == null || logItemView.BackgroundImage == null)
            {
                return;
            }

            var nextBackgroundColor = EvenLogItemBackgroundColor;
            if (_LogItemViews.Count > 0)
            {
                var previousBackgroundImage = _LogItemViews[_LogItemViews.Count - 1].BackgroundImage;
                var previousBackgroundColor = previousBackgroundImage == null
                    ? OddLogItemBackgroundColor
                    : previousBackgroundImage.color;
                nextBackgroundColor = previousBackgroundColor == EvenLogItemBackgroundColor
                    ? OddLogItemBackgroundColor
                    : EvenLogItemBackgroundColor;
            }

            logItemView.BackgroundImage.color = nextBackgroundColor;
        }

        private Color _ResolveLogTextColor(string logMessage)
        {
            switch (_ResolveLogSeverity(logMessage))
            {
                case GMLogSeverity.Debug:
                    return DebugLogTextColor;
                case GMLogSeverity.Warn:
                    return WarnLogTextColor;
                case GMLogSeverity.Error:
                    return ErrorLogTextColor;
                default:
                    return InfoLogTextColor;
            }
        }

        private GMLogSeverity _ResolveLogSeverity(string logMessage)
        {
            if (string.IsNullOrEmpty(logMessage))
            {
                return GMLogSeverity.Info;
            }

            if (_ContainsLogToken(logMessage, "[error]")
                || _ContainsLogToken(logMessage, "[fatal]")
                || _ContainsLogToken(logMessage, "[exception]")
                || _ContainsLogToken(logMessage, "[assert]"))
            {
                return GMLogSeverity.Error;
            }

            if (_ContainsLogToken(logMessage, "[warn]")
                || _ContainsLogToken(logMessage, "[warning]"))
            {
                return GMLogSeverity.Warn;
            }

            if (_ContainsLogToken(logMessage, "[debug]"))
            {
                return GMLogSeverity.Debug;
            }

            return GMLogSeverity.Info;
        }

        private bool _ContainsLogToken(string logMessage, string token)
        {
            return logMessage.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string _StripRichTextTags(string logMessage)
        {
            if (string.IsNullOrEmpty(logMessage))
            {
                return string.Empty;
            }

            return logMessage
                .Replace("</color>", string.Empty)
                .Replace("<color=#AEF0B8>", string.Empty)
                .Replace("<color=#FFFFFF>", string.Empty)
                .Replace("<color=#FFF2A6>", string.Empty)
                .Replace("<color=#FFB8B8>", string.Empty);
        }

        private string _BuildLogLine(string condition, string stackTrace, LogType logType)
        {
            var prefix = _GetLogTypePrefix(logType);
            if (string.IsNullOrWhiteSpace(stackTrace) || logType == LogType.Log || logType == LogType.Warning)
            {
                return prefix + " " + condition;
            }

            return prefix + " " + condition + "\n" + stackTrace;
        }

        private string _GetLogTypePrefix(LogType logType)
        {
            switch (logType)
            {
                case LogType.Warning:
                    return "[Warn]";
                case LogType.Error:
                    return "[Error]";
                case LogType.Assert:
                    return "[Assert]";
                case LogType.Exception:
                    return "[Exception]";
                default:
                    return "[Info]";
            }
        }

        private void _FocusCommandInput()
        {
            if (_CommandInputField == null)
            {
                return;
            }

            _CommandInputField.text = string.Empty;
            _CommandInputField.ActivateInputField();
            _CommandInputField.Select();
        }

        private readonly KeyCode _ToggleHotKey = KeyCode.BackQuote;
        private GameObject _PanelNode;
        private InputField _CommandInputField;
        private RectTransform _LogContentNode;
        private ScrollRect _LogScrollRect;
        private bool _FocusCommandInputNextFrame;

        private sealed class GMLogItemView
        {
            public GameObject RootNode;
            public Image BackgroundImage;
            public Text ContentText;
        }

        private enum GMLogSeverity
        {
            Debug = 0,
            Info = 1,
            Warn = 2,
            Error = 3,
        }
    }

    public partial class UIManager
    {
        /// <summary>
        /// Controller instance used to manage the performance data panel.
        /// </summary>
        private PerformanceDataPanelController _PerformanceDataPanelController;

        /// <summary>
        /// Controller responsible for displaying the GM panel and dispatching GM commands.
        /// </summary>
        private GMPanelController _GMPanelController;

        /// <summary>
        /// Raised when a GM command is dispatched. The argument is the GM command text.
        /// </summary>
        public event Action<string> GMCommandDispatched;

        private void _InitDebugInfoLayer()
        {
            InitPerformanceDataPanel();
            InitGMPanel();
        }

        private void InitPerformanceDataPanel()
        {
            var performanceDataPanelNode = new GameObject("PerformanceDataPanel", typeof(RectTransform), typeof(Text));
            performanceDataPanelNode.transform.SetParent(_DebugInfoLayerNode.transform, false);

            var rectTransform = performanceDataPanelNode.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.0f, 1.0f);
            rectTransform.anchorMax = new Vector2(0.0f, 1.0f);
            rectTransform.pivot = new Vector2(0.0f, 1.0f);
            rectTransform.anchoredPosition = new Vector2(20.0f, -20.0f);
            rectTransform.sizeDelta = new Vector2(320.0f, 80.0f);
            rectTransform.localScale = Vector3.one;

            var text = performanceDataPanelNode.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 24;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            text.text = "FPS: --\nMemory: -- MB";

            _PerformanceDataPanelController = _DebugInfoLayerNode.AddComponent<PerformanceDataPanelController>();
            _PerformanceDataPanelController.Bind(text);
            _PerformanceDataPanelController.Init();
        }

        private void InitGMPanel()
        {
            var gmPanelNode = new GameObject("GMPanel", typeof(RectTransform), typeof(Image));
            gmPanelNode.transform.SetParent(_DebugInfoLayerNode.transform, false);

            var gmPanelRectTransform = gmPanelNode.GetComponent<RectTransform>();
            _StretchToFullScreen(gmPanelRectTransform);

            var gmPanelImage = gmPanelNode.GetComponent<Image>();
            gmPanelImage.color = new Color(0.0f, 0.0f, 0.0f, 0.55f);

            var titleText = _CreateTextNode(
                "TitleText",
                gmPanelNode.transform,
                "GM Panel",
                24,
                TextAnchor.MiddleLeft,
                new Color(0.95f, 0.95f, 0.95f, 1.0f));
            var titleRectTransform = titleText.rectTransform;
            titleRectTransform.anchorMin = new Vector2(0.0f, 1.0f);
            titleRectTransform.anchorMax = new Vector2(1.0f, 1.0f);
            titleRectTransform.pivot = new Vector2(0.0f, 1.0f);
            titleRectTransform.offsetMin = new Vector2(40.0f, -60.0f);
            titleRectTransform.offsetMax = new Vector2(-40.0f, -20.0f);

            var logViewportNode = new GameObject("LogViewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            logViewportNode.transform.SetParent(gmPanelNode.transform, false);

            var logViewportRectTransform = logViewportNode.GetComponent<RectTransform>();
            logViewportRectTransform.anchorMin = new Vector2(0.0f, 0.0f);
            logViewportRectTransform.anchorMax = new Vector2(1.0f, 1.0f);
            logViewportRectTransform.offsetMin = new Vector2(40.0f, 96.0f);
            logViewportRectTransform.offsetMax = new Vector2(-56.0f, -76.0f);
            logViewportRectTransform.localScale = Vector3.one;

            var logViewportImage = logViewportNode.GetComponent<Image>();
            logViewportImage.color = new Color(0.0f, 0.0f, 0.0f, 0.72f);

            var logViewportMask = logViewportNode.GetComponent<Mask>();
            logViewportMask.showMaskGraphic = true;

            var logContentNode = new GameObject("LogContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            logContentNode.transform.SetParent(logViewportNode.transform, false);

            var logContentRectTransform = logContentNode.GetComponent<RectTransform>();
            logContentRectTransform.anchorMin = new Vector2(0.0f, 1.0f);
            logContentRectTransform.anchorMax = new Vector2(1.0f, 1.0f);
            logContentRectTransform.pivot = new Vector2(0.5f, 1.0f);
            logContentRectTransform.offsetMin = new Vector2(8.0f, 0.0f);
            logContentRectTransform.offsetMax = new Vector2(-8.0f, 0.0f);
            logContentRectTransform.anchoredPosition = Vector2.zero;
            logContentRectTransform.localScale = Vector3.one;

            var logContentVerticalLayoutGroup = logContentNode.GetComponent<VerticalLayoutGroup>();
            logContentVerticalLayoutGroup.childAlignment = TextAnchor.UpperLeft;
            logContentVerticalLayoutGroup.childControlWidth = true;
            logContentVerticalLayoutGroup.childControlHeight = true;
            logContentVerticalLayoutGroup.childForceExpandWidth = true;
            logContentVerticalLayoutGroup.childForceExpandHeight = false;
            logContentVerticalLayoutGroup.spacing = 0.0f;
            logContentVerticalLayoutGroup.padding = new RectOffset(0, 0, 0, 0);

            var logContentSizeFitter = logContentNode.GetComponent<ContentSizeFitter>();
            logContentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            logContentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = logViewportNode.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.viewport = logViewportRectTransform;
            scrollRect.content = logContentRectTransform;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24.0f;

            var scrollbarNode = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarNode.transform.SetParent(gmPanelNode.transform, false);

            var scrollbarRectTransform = scrollbarNode.GetComponent<RectTransform>();
            scrollbarRectTransform.anchorMin = new Vector2(1.0f, 0.0f);
            scrollbarRectTransform.anchorMax = new Vector2(1.0f, 1.0f);
            scrollbarRectTransform.pivot = new Vector2(1.0f, 1.0f);
            scrollbarRectTransform.offsetMin = new Vector2(-40.0f, 96.0f);
            scrollbarRectTransform.offsetMax = new Vector2(-24.0f, -76.0f);
            scrollbarRectTransform.localScale = Vector3.one;

            var scrollbarBackgroundImage = scrollbarNode.GetComponent<Image>();
            scrollbarBackgroundImage.color = new Color(1.0f, 1.0f, 1.0f, 0.15f);

            var handleSlidingAreaNode = new GameObject("SlidingArea", typeof(RectTransform));
            handleSlidingAreaNode.transform.SetParent(scrollbarNode.transform, false);

            var handleSlidingAreaRectTransform = handleSlidingAreaNode.GetComponent<RectTransform>();
            _StretchToFullScreen(handleSlidingAreaRectTransform);
            handleSlidingAreaRectTransform.offsetMin = new Vector2(2.0f, 2.0f);
            handleSlidingAreaRectTransform.offsetMax = new Vector2(-2.0f, -2.0f);

            var handleNode = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleNode.transform.SetParent(handleSlidingAreaNode.transform, false);

            var handleRectTransform = handleNode.GetComponent<RectTransform>();
            _StretchToFullScreen(handleRectTransform);

            var handleImage = handleNode.GetComponent<Image>();
            handleImage.color = new Color(0.75f, 0.78f, 0.82f, 0.95f);

            var scrollbar = scrollbarNode.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.targetGraphic = handleImage;
            scrollbar.handleRect = handleRectTransform;
            scrollbar.size = 1.0f;

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scrollRect.verticalScrollbarSpacing = 8.0f;

            var inputBackgroundNode = new GameObject("CommandInput", typeof(RectTransform), typeof(Image), typeof(InputField));
            inputBackgroundNode.transform.SetParent(gmPanelNode.transform, false);

            var inputBackgroundRectTransform = inputBackgroundNode.GetComponent<RectTransform>();
            inputBackgroundRectTransform.anchorMin = new Vector2(0.0f, 0.0f);
            inputBackgroundRectTransform.anchorMax = new Vector2(1.0f, 0.0f);
            inputBackgroundRectTransform.offsetMin = new Vector2(40.0f, 28.0f);
            inputBackgroundRectTransform.offsetMax = new Vector2(-152.0f, 68.0f);
            inputBackgroundRectTransform.localScale = Vector3.one;

            var inputBackgroundImage = inputBackgroundNode.GetComponent<Image>();
            inputBackgroundImage.color = new Color(1.0f, 1.0f, 1.0f, 0.95f);

            var inputText = _CreateTextNode(
                "Text",
                inputBackgroundNode.transform,
                string.Empty,
                20,
                TextAnchor.MiddleLeft,
                Color.black);
            var inputTextRectTransform = inputText.rectTransform;
            inputTextRectTransform.anchorMin = Vector2.zero;
            inputTextRectTransform.anchorMax = Vector2.one;
            inputTextRectTransform.offsetMin = new Vector2(12.0f, 6.0f);
            inputTextRectTransform.offsetMax = new Vector2(-12.0f, -6.0f);

            var placeholderText = _CreateTextNode(
                "Placeholder",
                inputBackgroundNode.transform,
                "Input GM command...",
                20,
                TextAnchor.MiddleLeft,
                new Color(0.55f, 0.55f, 0.55f, 1.0f));
            var placeholderRectTransform = placeholderText.rectTransform;
            placeholderRectTransform.anchorMin = Vector2.zero;
            placeholderRectTransform.anchorMax = Vector2.one;
            placeholderRectTransform.offsetMin = new Vector2(12.0f, 6.0f);
            placeholderRectTransform.offsetMax = new Vector2(-12.0f, -6.0f);
            placeholderText.fontStyle = FontStyle.Italic;

            var inputField = inputBackgroundNode.GetComponent<InputField>();
            inputField.textComponent = inputText;
            inputField.placeholder = placeholderText;
            inputField.lineType = InputField.LineType.SingleLine;

            var sendButtonNode = new GameObject("SendButton", typeof(RectTransform), typeof(Image), typeof(Button));
            sendButtonNode.transform.SetParent(gmPanelNode.transform, false);

            var sendButtonRectTransform = sendButtonNode.GetComponent<RectTransform>();
            sendButtonRectTransform.anchorMin = new Vector2(1.0f, 0.0f);
            sendButtonRectTransform.anchorMax = new Vector2(1.0f, 0.0f);
            sendButtonRectTransform.pivot = new Vector2(1.0f, 0.0f);
            sendButtonRectTransform.offsetMin = new Vector2(-132.0f, 28.0f);
            sendButtonRectTransform.offsetMax = new Vector2(-40.0f, 68.0f);
            sendButtonRectTransform.localScale = Vector3.one;

            var sendButtonImage = sendButtonNode.GetComponent<Image>();
            sendButtonImage.color = new Color(0.20f, 0.55f, 0.90f, 1.0f);

            var sendButtonText = _CreateTextNode(
                "Text",
                sendButtonNode.transform,
                "Send",
                20,
                TextAnchor.MiddleCenter,
                Color.white);
            _StretchToFullScreen(sendButtonText.rectTransform);

            _GMPanelController = _DebugInfoLayerNode.AddComponent<GMPanelController>();
            _GMPanelController.Bind(gmPanelNode, inputField, logContentRectTransform, scrollRect);
            _GMPanelController.GMCommandDispatched += _HandleGMCommandDispatched;
            _GMPanelController.Init();

            var sendButton = sendButtonNode.GetComponent<Button>();
            sendButton.onClick.AddListener(_GMPanelController.SendCurrentCommand);
            inputField.onEndEdit.AddListener(_GMPanelController.OnInputFieldEndEdit);
        }

        private void _HandleGMCommandDispatched(string gmCommand)
        {
            GMCommandDispatched?.Invoke(gmCommand);
        }

        private Text _CreateTextNode(string nodeName, Transform parentNode, string content, int fontSize, TextAnchor alignment, Color color)
        {
            var textNode = new GameObject(nodeName, typeof(RectTransform), typeof(Text));
            textNode.transform.SetParent(parentNode, false);

            var text = textNode.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = color;
            text.text = content;
            text.supportRichText = false;

            return text;
        }
    }
}
