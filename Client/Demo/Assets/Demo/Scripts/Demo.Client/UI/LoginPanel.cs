using Assets.Scripts.DE.Client.Core;
using Assets.Scripts.DE.Client.Framework;
using Assets.Scripts.DE.Client.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Demo.Client.UI
{
    [Panel("Demo/Asb/UI/Login/LoginPanel.prefab")]
    public class LoginPanel : PanelBase
    {
        protected override void Bind()
        {
            _AccountInputField = _FindChildComponent<TMP_InputField>(transform, "AccountInputField");
            _PasswordInputField = _FindChildComponent<TMP_InputField>(transform, "PasswordInputField");
            _LoginButton = _FindChildComponent<Button>(transform, "Button");

            if (_AccountInputField == null || _PasswordInputField == null || _LoginButton == null)
            {
                DELogger.Error("LoginPanel", "Bind failed because login UI components are missing.");
                return;
            }

            _LoginButton.onClick.AddListener(_OnLoginButtonClicked);
            DELogger.Info("LoginPanel", "Bind completed.");
        }

        protected override void OnShow(object arg)
        {
            DELogger.Info("LoginPanel", "OnShow");
        }

        protected override void OnHide()
        {
        }

        private void _OnLoginButtonClicked()
        {
            if (AuthSystem.Instance == null)
            {
                DELogger.Error("LoginPanel", "Login failed because AuthSystem is not initialized.");
                return;
            }

            var account = _AccountInputField == null ? string.Empty : _AccountInputField.text;
            var password = _PasswordInputField == null ? string.Empty : _PasswordInputField.text;
            AuthSystem.Instance.Login(account, password);
        }

        private T _FindChildComponent<T>(Transform parentNode, string childName) where T : Component
        {
            var childNode = _FindChildRecursive(parentNode, childName);
            return childNode == null ? null : childNode.GetComponent<T>();
        }

        private Transform _FindChildRecursive(Transform parentNode, string childName)
        {
            if (parentNode == null || string.IsNullOrEmpty(childName))
            {
                return null;
            }

            if (parentNode.name == childName)
            {
                return parentNode;
            }

            for (int index = 0; index < parentNode.childCount; index++)
            {
                var childNode = _FindChildRecursive(parentNode.GetChild(index), childName);
                if (childNode != null)
                {
                    return childNode;
                }
            }

            return null;
        }

        private TMP_InputField _AccountInputField;
        private TMP_InputField _PasswordInputField;
        private Button _LoginButton;
    }
}
