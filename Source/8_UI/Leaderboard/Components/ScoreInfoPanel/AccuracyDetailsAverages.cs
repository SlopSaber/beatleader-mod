using System;
using BeatSaberMarkupLanguage.Attributes;
using JetBrains.Annotations;

namespace BeatLeader.Components {
    internal class AccuracyDetailsAverages : ReeUIComponentV2 {
        #region Clear

        public void Clear() {
            PreScore = "";
            AccScore = "";
            PostScore = "";
        }

        #endregion

        #region SetValues

        public void SetValues(float preScore, float accScore, float postScore) {
            PreScore = FormatScore(preScore);
            AccScore = FormatScore(accScore);
            PostScore = FormatScore(postScore);
        }

        internal bool SetPreparedValues(string pre, string accuracy, string post, Func<bool> canApply) {
            if (!CanPublish()) return false;
            PreScore = pre;
            if (!CanPublish()) return false;
            AccScore = accuracy;
            if (!CanPublish()) return false;
            PostScore = post;
            return CanPublish();

            bool CanPublish() => canApply() && this && IsHierarchySet && Content;
        }

        #endregion

        #region Formatting

        private static string FormatScore(float value) {
            return $"{value:F2}";
        }

        #endregion

        #region PreScore

        private string _preScore = "";

        [UIValue("pre-score"), UsedImplicitly]
        private string PreScore {
            get => _preScore;
            set {
                if (_preScore.Equals(value)) return;
                _preScore = value;
                NotifyPropertyChanged();
            }
        }

        #endregion

        #region AccScore

        private string _accScore = "";

        [UIValue("acc-score"), UsedImplicitly]
        private string AccScore {
            get => _accScore;
            set {
                if (_accScore.Equals(value)) return;
                _accScore = value;
                NotifyPropertyChanged();
            }
        }

        #endregion

        #region PostScore

        private string _postScore = "";

        [UIValue("post-score"), UsedImplicitly]
        private string PostScore {
            get => _postScore;
            set {
                if (_postScore.Equals(value)) return;
                _postScore = value;
                NotifyPropertyChanged();
            }
        }

        #endregion
    }
}