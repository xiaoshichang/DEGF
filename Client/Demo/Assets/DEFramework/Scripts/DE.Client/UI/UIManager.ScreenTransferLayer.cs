using System;
using System.Collections;
using Assets.Scripts.DE.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.DE.Client.UI
{
    public enum ScreenTransferColorType
    {
        Black = 0,
        White = 1,
    }

    public class ScreenTransferLayerController : MonoBehaviour
    {
        public event Action<string> ScreenTransferEntered;
        public event Action<string> ScreenTransferExited;

        public void Bind(GameObject screenTransferMaskNode, Image screenTransferMaskImage)
        {
            _ScreenTransferMaskNode = screenTransferMaskNode;
            _ScreenTransferMaskImage = screenTransferMaskImage;
        }

        public void Init()
        {
            _CurrentSourceId = string.Empty;
            _CurrentScreenTransferColorType = ScreenTransferColorType.Black;
            _CurrentState = ScreenTransferState.Idle;
            _CurrentScreenTransferCoroutine = null;
            _SetScreenTransferVisible(false);
            _SetScreenTransferAlpha(0.0f);
        }

        public bool EnterScreenTransfer(string sourceId, float enterDuration, ScreenTransferColorType screenTransferColorType, Action<string> onEntered)
        {
            if (!_TryValidateSourceId(sourceId, "EnterScreenTransfer"))
            {
                return false;
            }

            if (_HasRunningOperation())
            {
                _LogOperationInProgress("EnterScreenTransfer", sourceId);
                return false;
            }

            if (_CurrentState != ScreenTransferState.Idle)
            {
                _LogConflict("EnterScreenTransfer", sourceId);
                return false;
            }

            _CurrentSourceId = sourceId;
            _CurrentScreenTransferColorType = screenTransferColorType;
            _CurrentScreenTransferCoroutine = StartCoroutine(_EnterScreenTransferCoroutine(enterDuration, onEntered));
            return true;
        }

        public bool ExitScreenTransfer(string sourceId, float exitDuration, Action<string> onExited)
        {
            if (!_TryValidateSourceId(sourceId, "ExitScreenTransfer"))
            {
                return false;
            }

            if (_HasRunningOperation())
            {
                _LogOperationInProgress("ExitScreenTransfer", sourceId);
                return false;
            }

            if (_CurrentState == ScreenTransferState.Idle || !_IsScreenTransferVisible())
            {
                DELogger.Error("UIManager", "ScreenTransfer is not active, sourceId=" + sourceId + " cannot exit.");
                return false;
            }

            if (!_IsCurrentSource(sourceId))
            {
                _LogConflict("ExitScreenTransfer", sourceId);
                return false;
            }

            _CurrentScreenTransferCoroutine = StartCoroutine(_ExitScreenTransferCoroutine(exitDuration, onExited));
            return true;
        }

        public bool EnterAndExitScreenTransfer(
            string sourceId,
            float enterDuration,
            float exitDuration,
            ScreenTransferColorType screenTransferColorType,
            Action<string> onEntered,
            Action<string> onExited)
        {
            if (!_TryValidateSourceId(sourceId, "EnterAndExitScreenTransfer"))
            {
                return false;
            }

            if (_HasRunningOperation())
            {
                _LogOperationInProgress("EnterAndExitScreenTransfer", sourceId);
                return false;
            }

            if (_CurrentState != ScreenTransferState.Idle)
            {
                _LogConflict("EnterAndExitScreenTransfer", sourceId);
                return false;
            }

            _CurrentSourceId = sourceId;
            _CurrentScreenTransferColorType = screenTransferColorType;
            _CurrentScreenTransferCoroutine = StartCoroutine(
                _EnterAndExitScreenTransferCoroutine(
                    enterDuration,
                    exitDuration,
                    onEntered,
                    onExited));
            return true;
        }

        private bool _TryValidateSourceId(string sourceId, string operationName)
        {
            if (!string.IsNullOrWhiteSpace(sourceId))
            {
                return true;
            }

            DELogger.Error("UIManager", operationName + " requires a valid sourceId.");
            return false;
        }

        private bool _HasRunningOperation()
        {
            return _CurrentScreenTransferCoroutine != null;
        }

        private bool _IsCurrentSource(string sourceId)
        {
            return string.Equals(_CurrentSourceId, sourceId, StringComparison.Ordinal);
        }

        private bool _IsScreenTransferVisible()
        {
            return _ScreenTransferMaskNode != null && _ScreenTransferMaskNode.activeSelf;
        }

        private void _LogConflict(string operationName, string sourceId)
        {
            DELogger.Error(
                "UIManager",
                operationName
                + " conflict, current sourceId="
                + _CurrentSourceId
                + ", request sourceId="
                + sourceId
                + ".");
        }

        private void _LogOperationInProgress(string operationName, string sourceId)
        {
            DELogger.Error(
                "UIManager",
                operationName
                + " failed because screen transfer is busy, current sourceId="
                + _CurrentSourceId
                + ", request sourceId="
                + sourceId
                + ", state="
                + _CurrentState
                + ".");
        }

        private IEnumerator _EnterScreenTransferCoroutine(float enterDuration, Action<string> onEntered)
        {
            _CurrentState = ScreenTransferState.Entering;
            _ApplyScreenTransferColor();
            _SetScreenTransferVisible(true);
            yield return _PlayFadeCoroutine(0.0f, 1.0f, enterDuration);
            _CurrentState = ScreenTransferState.Entered;
            _CurrentScreenTransferCoroutine = null;
            ScreenTransferEntered?.Invoke(_CurrentSourceId);
            onEntered?.Invoke(_CurrentSourceId);
        }

        private IEnumerator _ExitScreenTransferCoroutine(float exitDuration, Action<string> onExited)
        {
            _CurrentState = ScreenTransferState.Exiting;
            _ApplyScreenTransferColor();
            _SetScreenTransferVisible(true);
            yield return _PlayFadeCoroutine(1.0f, 0.0f, exitDuration);
            var completedSourceId = _CurrentSourceId;
            _CurrentSourceId = string.Empty;
            _CurrentState = ScreenTransferState.Idle;
            _CurrentScreenTransferCoroutine = null;
            _SetScreenTransferVisible(false);
            ScreenTransferExited?.Invoke(completedSourceId);
            onExited?.Invoke(completedSourceId);
        }

        private IEnumerator _EnterAndExitScreenTransferCoroutine(
            float enterDuration,
            float exitDuration,
            Action<string> onEntered,
            Action<string> onExited)
        {
            _CurrentState = ScreenTransferState.Entering;
            _ApplyScreenTransferColor();
            _SetScreenTransferVisible(true);
            yield return _PlayFadeCoroutine(0.0f, 1.0f, enterDuration);
            _CurrentState = ScreenTransferState.Entered;
            ScreenTransferEntered?.Invoke(_CurrentSourceId);
            onEntered?.Invoke(_CurrentSourceId);

            _CurrentState = ScreenTransferState.Exiting;
            yield return _PlayFadeCoroutine(1.0f, 0.0f, exitDuration);
            var completedSourceId = _CurrentSourceId;
            _CurrentSourceId = string.Empty;
            _CurrentState = ScreenTransferState.Idle;
            _CurrentScreenTransferCoroutine = null;
            _SetScreenTransferVisible(false);
            ScreenTransferExited?.Invoke(completedSourceId);
            onExited?.Invoke(completedSourceId);
        }

        private IEnumerator _PlayFadeCoroutine(float fromAlpha, float toAlpha, float duration)
        {
            var safeDuration = Mathf.Max(0.0f, duration);
            _SetScreenTransferAlpha(fromAlpha);

            if (safeDuration <= 0.0f)
            {
                _SetScreenTransferAlpha(toAlpha);
                yield break;
            }

            var elapsedTime = 0.0f;
            while (elapsedTime < safeDuration)
            {
                elapsedTime += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsedTime / safeDuration);
                _SetScreenTransferAlpha(Mathf.Lerp(fromAlpha, toAlpha, progress));
                yield return null;
            }

            _SetScreenTransferAlpha(toAlpha);
        }

        private void _ApplyScreenTransferColor()
        {
            if (_ScreenTransferMaskImage == null)
            {
                return;
            }

            var screenTransferColor = _CurrentScreenTransferColorType == ScreenTransferColorType.White
                ? Color.white
                : Color.black;
            screenTransferColor.a = _ScreenTransferMaskImage.color.a;
            _ScreenTransferMaskImage.color = screenTransferColor;
        }

        private void _SetScreenTransferAlpha(float alpha)
        {
            if (_ScreenTransferMaskImage == null)
            {
                return;
            }

            var color = _ScreenTransferMaskImage.color;
            color.a = Mathf.Clamp01(alpha);
            _ScreenTransferMaskImage.color = color;
        }

        private void _SetScreenTransferVisible(bool isVisible)
        {
            if (_ScreenTransferMaskNode != null)
            {
                _ScreenTransferMaskNode.SetActive(isVisible);
            }

            if (_ScreenTransferMaskImage != null)
            {
                _ScreenTransferMaskImage.raycastTarget = isVisible;
            }
        }

        private GameObject _ScreenTransferMaskNode;
        private Image _ScreenTransferMaskImage;
        private string _CurrentSourceId = string.Empty;
        private ScreenTransferColorType _CurrentScreenTransferColorType;
        private ScreenTransferState _CurrentState;
        private Coroutine _CurrentScreenTransferCoroutine;

        private enum ScreenTransferState
        {
            Idle = 0,
            Entering = 1,
            Entered = 2,
            Exiting = 3,
        }
    }

    public partial class UIManager
    {
        public bool EnterScreenTransfer(
            string sourceId,
            float enterDuration,
            ScreenTransferColorType screenTransferColorType,
            Action<string> onEntered = null)
        {
            if (_ScreenTransferLayerController == null)
            {
                DELogger.Error("UIManager", "ScreenTransferLayerController is not initialized.");
                return false;
            }

            return _ScreenTransferLayerController.EnterScreenTransfer(sourceId, enterDuration, screenTransferColorType, onEntered);
        }

        public bool ExitScreenTransfer(string sourceId, float exitDuration, Action<string> onExited = null)
        {
            if (_ScreenTransferLayerController == null)
            {
                DELogger.Error("UIManager", "ScreenTransferLayerController is not initialized.");
                return false;
            }

            return _ScreenTransferLayerController.ExitScreenTransfer(sourceId, exitDuration, onExited);
        }

        public bool EnterAndExitScreenTransfer(
            string sourceId,
            float enterDuration,
            float exitDuration,
            ScreenTransferColorType screenTransferColorType,
            Action<string> onEntered = null,
            Action<string> onExited = null)
        {
            if (_ScreenTransferLayerController == null)
            {
                DELogger.Error("UIManager", "ScreenTransferLayerController is not initialized.");
                return false;
            }

            return _ScreenTransferLayerController.EnterAndExitScreenTransfer(
                sourceId,
                enterDuration,
                exitDuration,
                screenTransferColorType,
                onEntered,
                onExited);
        }

        private void _InitScreenTransferLayer()
        {
            var screenTransferMaskNode = new GameObject("ScreenTransferMask", typeof(RectTransform), typeof(Image));
            screenTransferMaskNode.transform.SetParent(_ScreenTransferLayerNode.transform, false);

            var rectTransform = screenTransferMaskNode.GetComponent<RectTransform>();
            _StretchToFullScreen(rectTransform);

            var screenTransferMaskImage = screenTransferMaskNode.GetComponent<Image>();
            screenTransferMaskImage.color = Color.black;
            screenTransferMaskImage.raycastTarget = false;

            _ScreenTransferLayerController = _ScreenTransferLayerNode.AddComponent<ScreenTransferLayerController>();
            _ScreenTransferLayerController.Bind(screenTransferMaskNode, screenTransferMaskImage);
            _ScreenTransferLayerController.ScreenTransferEntered += _HandleScreenTransferEntered;
            _ScreenTransferLayerController.ScreenTransferExited += _HandleScreenTransferExited;
            _ScreenTransferLayerController.Init();
        }

        private void _HandleScreenTransferEntered(string sourceId)
        {
            ScreenTransferEntered?.Invoke(sourceId);
        }

        private void _HandleScreenTransferExited(string sourceId)
        {
            ScreenTransferExited?.Invoke(sourceId);
        }
    }
}
