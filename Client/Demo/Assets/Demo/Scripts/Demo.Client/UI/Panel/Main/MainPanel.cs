using Assets.Scripts.DE.Client.Framework;
using Assets.Scripts.DE.Client.UI;
using Assets.Scripts.Demo.Client.Entities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Demo.Client.UI
{
    [Panel("Demo/Asb/UI/Main/MainPanel.prefab")]
    public class MainPanel : PanelBase
    {
        protected override void Bind()
        {
            _AvatarInfoText = UIUtils.FindChildComponent<TMP_Text>(transform, "ResultText");
            _SetRandomHeadIconButton = UIUtils.FindChildComponent<Button>(transform, "SetRandomHeadIconButton");
            if (_SetRandomHeadIconButton == null)
            {
                _SetRandomHeadIconButton = _CreateSetRandomHeadIconButton();
            }

            if (_SetRandomHeadIconButton != null)
            {
                _SetRandomHeadIconButton.onClick.AddListener(_OnSetRandomHeadIconClicked);
            }
        }

        protected override void OnHide()
        {
            if (AuthSystem.Instance != null)
            {
                AuthSystem.Instance.AvatarUpdated -= _RefreshAvatarInfo;
            }
        }

        protected override void OnShow(object arg)
        {
            if (AuthSystem.Instance != null)
            {
                AuthSystem.Instance.AvatarUpdated -= _RefreshAvatarInfo;
                AuthSystem.Instance.AvatarUpdated += _RefreshAvatarInfo;
            }

            AuthAccountInfo accountInfo = arg as AuthAccountInfo;
            if (accountInfo == null && AuthSystem.Instance != null)
            {
                accountInfo = AuthSystem.Instance.CurrentAccountInfo;
            }

            _RefreshAvatarInfo(accountInfo);
        }

        private void _RefreshAvatarInfo(AuthAccountInfo accountInfo)
        {
            if (_AvatarInfoText == null)
            {
                return;
            }

            if (accountInfo == null || accountInfo.Avatar == null)
            {
                _AvatarInfoText.text = "Avatar Guid: --\nHeadIcon: --\nScore: --";
                return;
            }

            BasicInfoComponent basicInfo = (accountInfo.Avatar as DemoAvatarEntity)?.BasicInfo;
            string headIcon = basicInfo == null || string.IsNullOrEmpty(basicInfo.HeadIcon)
                ? "--"
                : basicInfo.HeadIcon;
            string score = basicInfo == null ? "--" : basicInfo.Score.ToString();

            _AvatarInfoText.text =
                "Avatar Guid: " + accountInfo.Avatar.Guid
                + "\nHeadIcon: " + headIcon
                + "\nScore: " + score;
        }

        private void _OnSetRandomHeadIconClicked()
        {
            if (AuthSystem.Instance == null)
            {
                return;
            }

            string headIcon = s_HeadIconCandidates[Random.Range(0, s_HeadIconCandidates.Length)];
            var avatar = AuthSystem.Instance.CurrentAccountInfo?.Avatar as DemoAvatarEntity;
            if (avatar == null)
            {
                return;
            }

            avatar.RequestSetHeadIcon(headIcon);
        }

        private Button _CreateSetRandomHeadIconButton()
        {
            var buttonNode = new GameObject("SetRandomHeadIconButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonNode.transform.SetParent(transform, false);
            var rectTransform = buttonNode.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 1.0f);
            rectTransform.anchorMax = new Vector2(0.5f, 1.0f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0.0f, -420.0f);
            rectTransform.sizeDelta = new Vector2(280.0f, 56.0f);

            var image = buttonNode.GetComponent<Image>();
            image.color = new Color(0.20f, 0.52f, 0.86f, 1.0f);

            var textNode = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMP_Text));
            textNode.transform.SetParent(buttonNode.transform, false);
            var textRectTransform = textNode.GetComponent<RectTransform>();
            textRectTransform.anchorMin = Vector2.zero;
            textRectTransform.anchorMax = Vector2.one;
            textRectTransform.offsetMin = Vector2.zero;
            textRectTransform.offsetMax = Vector2.zero;

            var label = textNode.GetComponent<TMP_Text>();
            label.text = "Random HeadIcon";
            label.fontSize = 24.0f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;

            return buttonNode.GetComponent<Button>();
        }

        private static readonly string[] s_HeadIconCandidates =
        {
            "head_icon_01.png",
            "head_icon_02.png",
            "head_icon_03.png",
            "head_icon_04.png",
            "head_icon_05.png",
        };

        private TMP_Text _AvatarInfoText;
        private Button _SetRandomHeadIconButton;
    }
}
