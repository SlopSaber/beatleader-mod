using System;
using BeatLeader.Models;
using BeatSaberMarkupLanguage.Attributes;
using JetBrains.Annotations;

namespace BeatLeader.Components {
    internal class AccuracyDetails : ReeUIComponentV2 {
        #region Components

        [UIValue("left-averages"), UsedImplicitly]
        private AccuracyDetailsAverages _leftAverages;

        [UIValue("left-pie-chart"), UsedImplicitly]
        private AccuracyPieChart _leftPieChart;

        [UIValue("right-averages"), UsedImplicitly]
        private AccuracyDetailsAverages _rightAverages;

        [UIValue("right-pie-chart"), UsedImplicitly]
        private AccuracyPieChart _rightPieChart;

        [UIValue("td-row"), UsedImplicitly]
        private AccuracyDetailsRow _tdRow;

        [UIValue("pre-row"), UsedImplicitly]
        private AccuracyDetailsRow _preRow;

        [UIValue("post-row"), UsedImplicitly]
        private AccuracyDetailsRow _postRow;

        private void Awake() {
            _leftAverages = Instantiate<AccuracyDetailsAverages>(transform);
            _leftPieChart = Instantiate<AccuracyPieChart>(transform);
            _rightAverages = Instantiate<AccuracyDetailsAverages>(transform);
            _rightPieChart = Instantiate<AccuracyPieChart>(transform);
            _tdRow = Instantiate<AccuracyDetailsRow>(transform);
            _preRow = Instantiate<AccuracyDetailsRow>(transform);
            _postRow = Instantiate<AccuracyDetailsRow>(transform);
        }

        #endregion

        #region SetScoreStats

        public void SetScoreStats(ScoreStats scoreStats) {
            var tracker = scoreStats.accuracyTracker;

            _leftAverages.SetValues(tracker.leftAverageCut[0], tracker.leftAverageCut[1], tracker.leftAverageCut[2]);
            _rightAverages.SetValues(tracker.rightAverageCut[0], tracker.rightAverageCut[1], tracker.rightAverageCut[2]);

            _leftPieChart.SetValues(AccuracyPieChart.Type.Left, tracker.accLeft);
            _rightPieChart.SetValues(AccuracyPieChart.Type.Right, tracker.accRight);

            _tdRow.SetValues(AccuracyDetailsRow.Type.TD, tracker.leftTimeDependence, tracker.rightTimeDependence);
            _preRow.SetValues(AccuracyDetailsRow.Type.Pre, tracker.leftPreswing, tracker.rightPreswing);
            _postRow.SetValues(AccuracyDetailsRow.Type.Post, tracker.leftPostswing, tracker.rightPostswing);
        }

        internal bool SetPreparedScoreStats(ScoreStatsPresentation values, Func<bool> canApply) {
            return CanPublish()
                && _leftAverages.SetPreparedValues(values.Averages[0], values.Averages[1], values.Averages[2], CanPublish)
                && _rightAverages.SetPreparedValues(values.Averages[3], values.Averages[4], values.Averages[5], CanPublish)
                && _leftPieChart.SetPreparedValues(AccuracyPieChart.Type.Left, values.Hands[0], CanPublish)
                && _rightPieChart.SetPreparedValues(AccuracyPieChart.Type.Right, values.Hands[1], CanPublish)
                && _tdRow.SetPreparedValues(values.Rows[0], CanPublish)
                && _preRow.SetPreparedValues(values.Rows[1], CanPublish)
                && _postRow.SetPreparedValues(values.Rows[2], CanPublish);

            bool CanPublish() => canApply() && this && IsHierarchySet && Content;
        }

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