#if __DEBUG__

using System.Globalization;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace Assets.Scripts.DE.Client.UI
{
    public class DebugInfoLayerController : MonoBehaviour
    {
        private const float RefreshInterval = 1f;
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

    public partial class UIManager
    {
        private void _InitDebugInfoLayer()
        {
            var debugInfoNode = new GameObject("DebugInfo", typeof(RectTransform), typeof(Text));
            debugInfoNode.transform.SetParent(_DebugInfoLayerNode.transform, false);

            var rectTransform = debugInfoNode.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.0f, 1.0f);
            rectTransform.anchorMax = new Vector2(0.0f, 1.0f);
            rectTransform.pivot = new Vector2(0.0f, 1.0f);
            rectTransform.anchoredPosition = new Vector2(20.0f, -20.0f);
            rectTransform.sizeDelta = new Vector2(320.0f, 80.0f);
            rectTransform.localScale = Vector3.one;

            var text = debugInfoNode.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 24;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            text.text = "FPS: --\nMemory: -- MB";

            _DebugInfoLayerController = _DebugInfoLayerNode.AddComponent<DebugInfoLayerController>();
            _DebugInfoLayerController.Bind(text);
            _DebugInfoLayerController.Init();
        }


        private DebugInfoLayerController _DebugInfoLayerController;
    }
}

#endif
