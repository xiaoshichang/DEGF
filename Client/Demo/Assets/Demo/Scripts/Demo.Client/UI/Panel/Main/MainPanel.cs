using Assets.Scripts.DE.Client.Framework;
using Assets.Scripts.DE.Client.UI;
using Assets.Scripts.Demo.Client.Entities;
using TMPro;
using UnityEngine;

namespace Assets.Scripts.Demo.Client.UI
{
    [Panel("Demo/Asb/UI/Main/MainPanel.prefab")]
    public class MainPanel : PanelBase
    {
        protected override void Bind()
        {
            _AvatarInfoText = UIUtils.FindChildComponent<TMP_Text>(transform, "ResultText");
        }

        protected override void OnHide()
        {
        }

        protected override void OnShow(object arg)
        {
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



        private TMP_Text _AvatarInfoText;
    }
}
