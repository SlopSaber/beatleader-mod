using System;
using System.Text;
using BeatLeader.Models;
using BeatSaberMarkupLanguage.Attributes;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;

namespace BeatLeader.Components {
    internal class ScoreOverviewPage2 : ReeUIComponentV2 {
        #region SetScoreAndStats

        public void SetScoreAndStats(Score score, ScoreStats scoreStats) {
            _platformTextComponent.text = FormatPlatformText(score.platform);

            _detailsTextComponent.text = FormatDetailsString(
                scoreStats.winTracker.jumpDistance,
                scoreStats.winTracker.averageHeight
            );

            _xTextComponent.text = FormatPositionString(scoreStats.winTracker.averageHeadPosition.x, "X", XColor);
            _yTextComponent.text = FormatPositionString(scoreStats.winTracker.averageHeadPosition.y, "Y",  YColor);
            _zTextComponent.text = FormatPositionString(scoreStats.winTracker.averageHeadPosition.z, "Z", ZColor);
        }

        internal bool SetPreparedScoreStats(ScoreStatsPresentation values, Func<bool> canApply) {
            if (!CanPublish()) return false;
            _platformTextComponent.text = values.PlatformText;
            if (!CanPublish()) return false;
            _detailsTextComponent.text = values.DetailsText;
            if (!CanPublish()) return false;
            _xTextComponent.text = values.XText;
            if (!CanPublish()) return false;
            _yTextComponent.text = values.YText;
            if (!CanPublish()) return false;
            _zTextComponent.text = values.ZText;
            return CanPublish();

            bool CanPublish() => canApply() && this && IsHierarchySet && Content;
        }

        #endregion

        #region Formatting

        private const string Neutral = "#FFFFFF";
        private const string Faded = "#888888";
        private const string XColor = "#FF8888";
        private const string YColor = "#88FF88";
        private const string ZColor = "#8888FF";

        private static string FormatPlatformText(string rawPlatform) {
            var sb = new StringBuilder();

            var split = rawPlatform.Split(',');
            if (split.Length < 3) {
                sb.Append($"<color={Faded}><bll>ls-user-platform</bll>: <color={Neutral}><bll>ls-unknown</bll> ");
            } else {
                var platformName = FormatUtils.GetFullPlatformName(split[0]);
                sb.Append($"<color={Faded}><bll>ls-user-platform</bll>: <color={Neutral}>{platformName}");
                sb.Append($"\r\n<color={Faded}><bll>ls-game</bll>: <color={Neutral}>{split[1]}    ");
                sb.Append($"<color={Faded}><bll>ls-mod</bll>: <color={Neutral}>{split[2]}    ");
            }

            return sb.ToString();
        }

        private static string FormatDetailsString(float jd, float height) {
            var sb = new StringBuilder();
            sb.Append($"<color={Faded}>JD: <color={Neutral}>{jd:F2}    ");
            sb.Append($"<color={Faded}><bll>ls-user-height</bll>: <color={Neutral}>{height:F2}<size=70%>m</size>    ");
            return sb.ToString();
        }

        private static string FormatPositionString(float value, string label, string color) {
            var sb = new StringBuilder();
            sb.Append($"<color={color}><size=80%>{label}</size>  {value:F2}<size=70%>m</size>    ");
            return sb.ToString();
        }

        #endregion

        #region Components

        [UIComponent("platform-text"), UsedImplicitly]
        private TextMeshProUGUI _platformTextComponent = default!;

        [UIComponent("details-text"), UsedImplicitly]
        private TextMeshProUGUI _detailsTextComponent = default!;

        [UIComponent("x-text-component"), UsedImplicitly]
        private TextMeshProUGUI _xTextComponent = default!;

        [UIComponent("y-text-component"), UsedImplicitly]
        private TextMeshProUGUI _yTextComponent = default!;

        [UIComponent("z-text-component"), UsedImplicitly]
        private TextMeshProUGUI _zTextComponent = default!;

        #endregion

        #region SetActive

        public void SetActive(bool value) {
            Active = value;
        }

        #endregion

        #region Active

        private bool _active = true;

        [UIValue("active"), UsedImplicitly]
        private bool Active {
            get => _active;
            set {
                if (_active.Equals(value)) return;
                _active = value;
                NotifyPropertyChanged();
            }
        }

        #endregion
    }
}