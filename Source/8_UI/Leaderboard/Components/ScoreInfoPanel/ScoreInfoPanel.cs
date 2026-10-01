using System;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.API;
using BeatLeader.DataManager;
using BeatLeader.Manager;
using BeatLeader.Models;
using BeatLeader.Utils;
using BeatSaberMarkupLanguage.Attributes;
using HMUI;
using JetBrains.Annotations;
using UnityEngine;

namespace BeatLeader.Components {
    internal class ScoreInfoPanel : AbstractReeModal<(Score, ReplayerViewNavigatorWrapper)> {
        #region Components

        [UIValue("mini-profile"), UsedImplicitly]
        private MiniProfile _miniProfile = null!;

        [UIValue("score-stats-loading-screen"), UsedImplicitly]
        private ScoreStatsLoadingScreen _scoreStatsLoadingScreen = null!;

        [UIValue("score-overview-page1"), UsedImplicitly]
        private ScoreOverviewPage1 _scoreOverviewPage1 = null!;

        [UIValue("score-overview-page2"), UsedImplicitly]
        private ScoreOverviewPage2 _scoreOverviewPage2 = null!;

        [UIValue("accuracy-details"), UsedImplicitly]
        private AccuracyDetails _accuracyDetails = null!;

        [UIValue("accuracy-grid"), UsedImplicitly]
        private AccuracyGrid _accuracyGrid = null!;

        [UIValue("accuracy-graph"), UsedImplicitly]
        private AccuracyGraphPanel _accuracyGraphPanel = null!;

        [UIValue("replay-panel"), UsedImplicitly]
        private ReplayPanel _replayPanel = null!;

        [UIValue("controls"), UsedImplicitly]
        private ScoreInfoPanelControls _controls = null!;

        [UIObject("accuracy-graph-container"), UsedImplicitly]
        private GameObject _accuracyGraphContainer = null!;

        [UIComponent("middle-panel"), UsedImplicitly]
        private ImageView _middlePanel = null!;

        [UIComponent("bottom-panel"), UsedImplicitly]
        private ImageView _bottomPanel = null!;

        private void Awake() {
            _miniProfile = Instantiate<MiniProfile>(transform);
            _scoreStatsLoadingScreen = Instantiate<ScoreStatsLoadingScreen>(transform);
            _scoreOverviewPage1 = Instantiate<ScoreOverviewPage1>(transform);
            _scoreOverviewPage2 = Instantiate<ScoreOverviewPage2>(transform);
            _accuracyDetails = Instantiate<AccuracyDetails>(transform);
            _accuracyGrid = Instantiate<AccuracyGrid>(transform);
            _accuracyGraphPanel = Instantiate<AccuracyGraphPanel>(transform);
            _replayPanel = Instantiate<ReplayPanel>(transform);
            _controls = Instantiate<ScoreInfoPanelControls>(transform);

            _replayPanel.DownloadStateChangedEvent += OnReplayDownloadStateChangedEvent;
        }

        #endregion

        #region Init / Dispose

        protected override void OnInitialize() {
            _disposed = false;
            base.OnInitialize();
            _middlePanel.raycastTarget = true;
            _bottomPanel.raycastTarget = true;

            ScoreStatsRequest.Request.StateChangedEvent += OnScoreStatsRequestStateChanged;
            LeaderboardState.ScoreInfoPanelTabChangedEvent += OnTabWasSelected;
            HiddenPlayersCache.HiddenPlayersUpdatedEvent += RefreshPlayer;
            OnTabWasSelected(LeaderboardState.ScoreInfoPanelTab);
        }

        protected override void OnDispose() {
            _disposed = true;
            InvalidateScoreStats();
            ScoreStatsRequest.Request.StateChangedEvent -= OnScoreStatsRequestStateChanged;
            HiddenPlayersCache.HiddenPlayersUpdatedEvent -= RefreshPlayer;
            LeaderboardState.ScoreInfoPanelTabChangedEvent -= OnTabWasSelected;
        }

        #endregion

        #region Events

        protected override void OnResume() {
            SetScore(Context.Item1);
            _replayPanel.Setup(Context.Item2);
        }

        protected override void OnClose() {
            InvalidateScoreStats();
            base.OnClose();
        }

        protected override void OnPause() {
            InvalidateScoreStats();
            base.OnPause();
        }

        protected override void OnInterrupt() {
            InvalidateScoreStats();
            base.OnInterrupt();
        }

        private void OnReplayDownloadStateChangedEvent(bool state) {
            offClickCloses = !state;
        }

        private void OnTabWasSelected(ScoreInfoPanelTab tab) {
            switch (tab) {
                case ScoreInfoPanelTab.OverviewPage1:
                case ScoreInfoPanelTab.Replay:
                    break;

                case ScoreInfoPanelTab.OverviewPage2:
                case ScoreInfoPanelTab.Accuracy:
                case ScoreInfoPanelTab.Grid:
                case ScoreInfoPanelTab.Graph:
                    if (_score != null && _scoreStatsUpdateRequired) {
                        LeaderboardEvents.RequestScoreStats(_score.id);
                    }

                    break;
                default: throw new ArgumentOutOfRangeException(nameof(tab), tab, null);
            }

            UpdateVisibility();
        }

        #endregion

        #region UpdateVisibility

        private void UpdateVisibility() {
            _scoreStatsLoadingScreen.SetActive(false);
            _scoreOverviewPage1.SetActive(false);
            _scoreOverviewPage2.SetActive(false);
            _accuracyDetails.SetActive(false);
            _accuracyGrid.SetActive(false);
            _accuracyGraphContainer.SetActive(false);
            _replayPanel.SetActive(LeaderboardState.ScoreInfoPanelTab == ScoreInfoPanelTab.Replay);

            switch (LeaderboardState.ScoreInfoPanelTab) {
                case ScoreInfoPanelTab.OverviewPage1:
                    _scoreOverviewPage1.SetActive(true);
                    break;
                case ScoreInfoPanelTab.OverviewPage2:
                    _scoreOverviewPage2.SetActive(!_scoreStatsUpdateRequired);
                    _scoreStatsLoadingScreen.SetActive(_scoreStatsUpdateRequired);
                    break;
                case ScoreInfoPanelTab.Accuracy:
                    _accuracyDetails.SetActive(!_scoreStatsUpdateRequired);
                    _scoreStatsLoadingScreen.SetActive(_scoreStatsUpdateRequired);
                    break;
                case ScoreInfoPanelTab.Grid:
                    _accuracyGrid.SetActive(!_scoreStatsUpdateRequired);
                    _scoreStatsLoadingScreen.SetActive(_scoreStatsUpdateRequired);
                    break;
                case ScoreInfoPanelTab.Graph:
                    _accuracyGraphContainer.SetActive(!_scoreStatsUpdateRequired);
                    _scoreStatsLoadingScreen.SetActive(_scoreStatsUpdateRequired);
                    break;
                case ScoreInfoPanelTab.Replay:
                    break;
                default: throw new InvalidOperationException($"Unknown score information tab: {LeaderboardState.ScoreInfoPanelTab}");
            }
        }

        #endregion

        #region SetScore

        private bool _scoreStatsUpdateRequired;
        private Score? _score;
        private int _scoreStatsRevision;
        private bool _disposed;
        private CancellationTokenSource? _scoreStatsCancellation;

        private void OnScoreStatsRequestStateChanged(WebRequests.IWebRequest<ScoreStats> instance, WebRequests.RequestState state, string? failReason) {
            if (_score == null || state is not WebRequests.RequestState.Finished) return;
            if (instance.Result is not { } result) return;
            InvalidateScoreStats();
            var score = _score;
            var revision = _scoreStatsRevision;
            if (!IsCurrentScoreStats(revision, score)) return;
            var source = new CancellationTokenSource();
            _scoreStatsCancellation = source;
            _ = ApplyScoreStatsAsync(score, result, revision, source).RunCatching();
        }

        private async Task ApplyScoreStatsAsync(Score score, ScoreStats result, int revision, CancellationTokenSource source) {
            var token = source.Token;
            try {
                var displayTask = ScoreStatsPresentation.PrepareAsync(score.platform, result, token);
                var graphTask = _accuracyGraphPanel.PrepareScoreStatsAsync(result, token);
                await Task.WhenAll(displayTask, graphTask);
                var display = await displayTask;
                var prepared = await graphTask;
                if (prepared == null || !CanApply() || !_accuracyGraphPanel.CanApplyPrepared(prepared)) return;
                if (!_scoreOverviewPage2.SetPreparedScoreStats(display, CanApply)
                    || !_accuracyDetails.SetPreparedScoreStats(display, CanApply)
                    || !_accuracyGrid.SetPreparedScoreStats(display, CanApply)
                    || !CanApply() || !_accuracyGraphPanel.SetPreparedScoreStats(prepared) || !CanApply()) return;
                _scoreStatsUpdateRequired = false;
                UpdateVisibility();

                bool CanApply() => !token.IsCancellationRequested && IsCurrentScoreStats(revision, score);
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            } catch (Exception ex) {
                Plugin.Log.Error($"Failed to prepare score statistics: {ex}");
                if (!token.IsCancellationRequested && IsCurrentScoreStats(revision, score)) {
                    _scoreStatsUpdateRequired = true;
                    _scoreStatsLoadingScreen.SetFailed(true);
                    if (IsCurrentScoreStats(revision, score)) UpdateVisibility();
                }
            } finally {
                if (ReferenceEquals(_scoreStatsCancellation, source)) _scoreStatsCancellation = null;
                source.Dispose();
            }
        }

        private bool IsCurrentScoreStats(int revision, Score score) {
            return revision == _scoreStatsRevision && ReferenceEquals(_score, score)
                && !_disposed && this && IsHierarchySet && Content && gameObject.activeInHierarchy;
        }

        private void InvalidateScoreStats() {
            _scoreStatsRevision++;
            var previous = _scoreStatsCancellation;
            _scoreStatsCancellation = null;
            previous?.Cancel();
            _accuracyGraphPanel?.InvalidatePreparation();
        }

        public void SetScore(Score score) {
            InvalidateScoreStats();
            _score = score;
            _scoreStatsUpdateRequired = true;
            _miniProfile.SetPlayer(score.Player);
            _scoreOverviewPage1.SetScore(score);
            _replayPanel.SetScore(score);
            _controls.Reset();
            UpdateVisibility();
        }

        private void RefreshPlayer() {
            if (_score is { } score) _miniProfile.SetPlayer(score.Player);
        }

        #endregion
    }
}
