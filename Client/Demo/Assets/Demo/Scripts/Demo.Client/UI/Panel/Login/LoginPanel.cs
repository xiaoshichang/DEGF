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
            _AccountInputField = UIUtils.FindChildComponent<TMP_InputField>(transform, "AccountInputField");
            _PasswordInputField = UIUtils.FindChildComponent<TMP_InputField>(transform, "PasswordInputField");
            _LoginButton = UIUtils.FindChildComponent<Button>(transform, "Button");

            if (_AccountInputField == null || _PasswordInputField == null || _LoginButton == null)
            {
                DELogger.Error("LoginPanel", "Bind failed because login UI components are missing.");
                return;
            }

            _LoginButton.onClick.AddListener(_OnLoginButtonClicked);
        }

        protected override void OnShow(object arg)
        {
            _AccountInputField.text = "xiao";
            _PasswordInputField.text = "pass";
            _RefreshLoginButton();

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
            AuthSystem.Instance.Login(account, password, _OnLoginStateChanged, _OnLoginSucceeded, _OnLoginFailed);
        }

        private void _RefreshLoginButton()
        {
            if (_LoginButton == null || AuthSystem.Instance == null)
            {
                return;
            }

            _LoginButton.interactable = !AuthSystem.Instance.IsBusy;
        }

        private void _OnLoginStateChanged(AuthState state)
        {
            _RefreshLoginButton();
        }

        private void _OnLoginSucceeded(AuthAccountInfo accountInfo)
        {
            _RefreshLoginButton();
            UIManager.Instance.PushPanel<MainPanel>(accountInfo);
        }

        private void _OnLoginFailed(string message)
        {
            _RefreshLoginButton();
            DELogger.Error("LoginPanel", message);
        }


        private TMP_InputField _AccountInputField;
        private TMP_InputField _PasswordInputField;
        private Button _LoginButton;
    }
}
