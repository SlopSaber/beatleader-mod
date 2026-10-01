using System;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Interop;
using BeatLeader.Models;
using BeatSaberMarkupLanguage.Attributes;
using JetBrains.Annotations;
using BeatLeader.Models.Replay;
using BeatLeader.UI;
using BeatLeader.Utils;
using Reactive;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = Reactive.BeatSaber.Components.Image;
using BeatLeader.API;
using HMUI;
using BeatLeader.WebRequests;
using IPA.Utilities.Async;
using Reactive.BeatSaber.Components;

namespace BeatLeader.Components {
    internal class ReplayPanel : ReeUIComponentV2 {
        #region UI Components

        [UIComponent("download-text"), UsedImplicitly]
        private TMP_Text _downloadText = null!;

        [UIComponent("play-button"), UsedImplicitly]
        private Button _playButton = null!;

        [UIComponent("play-button"), UsedImplicitly]
        private TMP_Text _playButtonText = null!;

        [UIComponent("download-button"), UsedImplicitly]
        private Button _downloadButton = null!;

        [UIComponent("download-button"), UsedImplicitly]
        private TMP_Text _downloadButtonText = null!;

        private Image _downloadButtonImage = null!;
        private Spinner _downloadButtonSpinner = null!;
        private HoverHint _downloadButtonHint = null!;

        [UIValue("settings-panel"), UsedImplicitly]
        private ReplayerSettingsPanel _settingsPanel = null!;

        #endregion

        #region Events

        public event Action<bool>? DownloadStateChangedEvent;

        private void NotifyDownloadStateChanged(bool state) {
            DownloadStateChangedEvent?.Invoke(state);
        }

        #endregion

        #region Initialize/Dispose

        private ReplayerViewNavigatorWrapper? _replayerNavigator;
        private bool _disposed;
        private bool _isChecking;
        private long _revision;
        private ReplayJob? _job;

        public void Setup(ReplayerViewNavigatorWrapper starter) {
            _replayerNavigator = starter;
        }

        protected override void OnInstantiate() {
            _settingsPanel = Instantiate<ReplayerSettingsPanel>(transform);
        }

        protected override void OnInitialize() {
            _disposed = false;
            _playButton.onClick.AddListener(OnPlayButtonClicked);
            _downloadButton.onClick.AddListener(OnDownloadButtonClicked);

            _downloadButtonImage = new Image {
                Sprite = BundleLoader.SaveIcon,
                Color = Color.white * 0.8f,
                PreserveAspect = true,
                Skew = UIStyle.Skew
            }.With(x => {
                    x.WithNativeComponent(out LayoutElement el);
                    el.preferredHeight = 6f;
                    el.preferredWidth = 6f;
                }
            );
            
            _downloadButtonSpinner = new Spinner {
                Image = {
                    Color = Color.white * 0.8f,
                    PreserveAspect = true,
                }
            }.With(x => {
                    x.WithNativeComponent(out LayoutElement el);
                    el.preferredHeight = 4.5f;
                    el.preferredWidth = 4.5f;
                }
            );

            _downloadButtonHint = _downloadButton.GetComponent<HoverHint>();

            var textParent = _downloadButtonText.transform.parent;
            _downloadButtonImage.Use(textParent);
            _downloadButtonSpinner.Use(textParent);

            LeaderboardState.AddSelectedBeatmapListener(OnSelectedBeatmapChanged);
        }

        protected override void OnDispose() {
            _disposed = true;
            RetireJob();
            _playButton.onClick.RemoveListener(OnPlayButtonClicked);
            _downloadButton.onClick.RemoveListener(OnDownloadButtonClicked);
            LeaderboardState.RemoveSelectedBeatmapListener(OnSelectedBeatmapChanged);
        }

        protected override void OnRootStateChange(bool active) {
            if (active) BeginChecking();
            else RetireJob();
        }

        #endregion

        #region Jobs

        private sealed class ReplayJob {
            public readonly Score Score;
            public readonly LeaderboardKey Leaderboard;
            public readonly long Revision;
            public readonly CancellationTokenSource Cancellation = new();
            public readonly CancellationToken Token;
            public IWebRequest<Replay>? Request;
            public WebRequestProgressChangedDelegate<IWebRequest<Replay>>? ProgressListener;
            public bool Finished;

            public ReplayJob(Score score, long revision) {
                Score = score;
                Leaderboard = LeaderboardState.SelectedLeaderboardKey;
                Revision = revision;
                Token = Cancellation.Token;
            }
        }

        private bool CanPresent() {
            return !_disposed && this && IsHierarchySet && Content && Content.gameObject.activeInHierarchy && _active;
        }

        private bool IsCurrent(ReplayJob job) {
            return ReferenceEquals(_job, job) && _revision == job.Revision
                && ReferenceEquals(_score, job.Score) && !job.Token.IsCancellationRequested
                && job.Leaderboard.Equals(LeaderboardState.SelectedLeaderboardKey) && CanPresent();
        }

        private ReplayJob? BeginJob() {
            RetireJob();
            if (_score == null || !CanPresent()) return null;
            var job = new ReplayJob(_score, _revision);
            _job = job;
            return job;
        }

        private void RetireJob() {
            var job = _job;
            _job = null;
            _revision++;
            var wasDownloading = _isDownloading;
            _isDownloading = false;
            _isChecking = false;
            if (job != null) {
                DetachProgress(job);
                if (!job.Finished) job.Cancellation.Cancel();
            }
            if (wasDownloading) NotifyDownloadStateChanged(false);
        }

        private static void DetachProgress(ReplayJob job) {
            if (job.Request != null && job.ProgressListener != null) {
                job.Request.ProgressChangedEvent -= job.ProgressListener;
                job.ProgressListener = null;
            }
        }

        private static void FinishJob(ReplayJob job) {
            job.Finished = true;
            DetachProgress(job);
            job.Request?.Dispose();
            job.Cancellation.Dispose();
        }

        private void SetIdle(ReplayJob job) {
            if (!IsCurrent(job)) return;
            _isChecking = false;
            var wasDownloading = _isDownloading;
            _isDownloading = false;
            if (wasDownloading) NotifyDownloadStateChanged(false);
            if (IsCurrent(job)) ResetButtons();
        }

        private void FailJob(ReplayJob job, Exception error) {
            Plugin.Log.Error(error);
            SetIdle(job);
            if (IsCurrent(job)) {
                _downloadText.gameObject.SetActive(true);
                _downloadText.text = FormatFailString(error.Message);
            }
        }

        #endregion

        #region SetScore

        private Score? _score;
        private IReplayHeader? _replayHeader;

        public void SetScore(Score score) {
            RetireJob();
            _score = score;
            _replayHeader = null;
            ResetButtons();
            BeginChecking();
        }

        private void BeginChecking() {
            if (!CanPresent() || _score == null || _job is { Finished: false }) return;
            var job = BeginJob();
            if (job == null) return;
            _isChecking = true;
            RefreshDownloadButton(DownloadButtonState.Unavailable);
            RefreshPlayButton(PlayButtonState.Unavailable);
            _ = FindLocalReplayAsync(job).RunCatching();
        }

        private async Task FindLocalReplayAsync(ReplayJob job) {
            try {
                await Task.Yield();
                if (!IsCurrent(job)) return;
                ReplayManager.StartLoadingIfNeverLoaded();
                var found = await ReplayManager.FindReplaysByHashAsync(new IReplayHashProvider[] { job.Score }, job.Token);
                if (!IsCurrent(job)) return;
                _replayHeader = found.Headers[0];
                SetIdle(job);
            } catch (OperationCanceledException) when (job.Token.IsCancellationRequested) {
            } catch (Exception error) {
                FailJob(job, error);
            } finally {
                FinishJob(job);
            }
        }

        #endregion

        #region StartReplay

        private async Task StartReplayAsync(ReplayJob job, Replay replay, Player player, int scoreId, ReplayerViewNavigatorWrapper navigator) {
            if (!IsCurrent(job)) return;
            await navigator.NavigateToReplayAsync(replay, player, true);
            SendViewReplayRequest.Send(scoreId);
        }

        private async Task LoadAndStartReplayAsync(ReplayJob job, IReplayHeader header) {
            try {
                await Task.Yield();
                if (!IsCurrent(job)) return;
                var player = job.Score.Player;
                var scoreId = job.Score.id;
                var navigator = _replayerNavigator ?? throw new InvalidOperationException("Replay navigator is unavailable");
                var replay = await header.LoadReplayAsync(job.Token);
                if (!IsCurrent(job)) return;
                if (replay == null) throw new InvalidOperationException("Failed to load the replay");
                await StartReplayAsync(job, replay, player, scoreId, navigator);
                SetIdle(job);
            } catch (OperationCanceledException) when (job.Token.IsCancellationRequested) {
            } catch (Exception error) {
                FailJob(job, error);
            } finally {
                FinishJob(job);
            }
        }

        #endregion

        #region Download

        private bool _isDownloading;

        private void OnSelectedBeatmapChanged(bool selectedAny, LeaderboardKey leaderboardKey, BeatmapKey key, BeatmapLevel level) {
            _playCanBeInteractable = SongCoreInterop.ValidateRequirements(new(level, key));
            if (_job != null && !_job.Leaderboard.Equals(leaderboardKey)) {
                RetireJob();
                _replayHeader = null;
                _isChecking = true;
                if (CanPresent()) {
                    RefreshDownloadButton(DownloadButtonState.Unavailable);
                    RefreshPlayButton(PlayButtonState.Unavailable);
                }
            }
        }

        private void SubscribeProgress(ReplayJob job, IWebRequest<Replay> request) {
            job.ProgressListener = (instance, download, upload, overall) => {
                _ = UnityMainThreadTaskScheduler.Factory.StartNew(() => {
                    if (job.ProgressListener != null && ReferenceEquals(job.Request, instance) && IsCurrent(job)) {
                        _downloadText.text = $"<alpha=#66>Downloading: {download * 100:F0}%";
                    }
                }).RunCatching();
            };
            request.ProgressChangedEvent += job.ProgressListener;
        }

        private void StartDownload(bool startReplay) {
            var job = BeginJob();
            if (job == null) return;
            _isDownloading = true;
            _downloadText.gameObject.SetActive(true);
            _downloadText.text = "<alpha=#66>Starting...";
            RefreshPlayButton(startReplay ? PlayButtonState.Downloading : PlayButtonState.Unavailable);
            RefreshDownloadButton(startReplay ? DownloadButtonState.Unavailable : DownloadButtonState.Downloading);
            NotifyDownloadStateChanged(true);
            _ = DownloadAsync(job, startReplay).RunCatching();
        }

        private async Task DownloadAsync(ReplayJob job, bool startReplay) {
            try {
                await Task.Yield();
                if (!IsCurrent(job)) return;
                var player = job.Score.Player;
                var scoreId = job.Score.id;
                var replayUrl = job.Score.replay;
                var navigator = _replayerNavigator ?? throw new InvalidOperationException("Replay navigator is unavailable");
                var request = DownloadReplayRequest.SendRequest(replayUrl, job.Token);
                job.Request = request;
                SubscribeProgress(job, request);
                var response = await request.Join();
                if (!IsCurrent(job)) return;
                DetachProgress(job);
                if (response.RequestState != WebRequests.RequestState.Finished || response.Result is not { } replay) {
                    throw new InvalidOperationException(response.FailReason ?? "Failed to download the replay");
                }
                RefreshDownloadButton(DownloadButtonState.Unavailable);
                RefreshPlayButton(PlayButtonState.Unavailable);
                if (startReplay) {
                    _downloadText.text = "<alpha=#66>Finished!";
                    await StartReplayAsync(job, replay, player, scoreId, navigator);
                } else {
                    _downloadText.text = "<alpha=#66>Saving...";
                    var result = await ReplayManager.SaveAnyReplayAsync(replay, null, job.Token);
                    if (!IsCurrent(job)) return;
                    var header = result.Header;
                    if (result.Error is ReplaySavingError.AlreadyExists) {
                        var existing = await ReplayManager.FindReplaysByHashAsync(new IReplayHashProvider[] { replay.info }, job.Token);
                        if (!IsCurrent(job)) return;
                        header = existing.Headers[0];
                    }
                    if (header == null) throw new InvalidOperationException("Failed to save the replay");
                    _replayHeader = header;
                    _downloadText.text = "<alpha=#66>Finished!";
                }
                SetIdle(job);
            } catch (OperationCanceledException) when (job.Token.IsCancellationRequested) {
            } catch (Exception error) {
                FailJob(job, error);
            } finally {
                FinishJob(job);
            }
        }

        #endregion

        #region Button Callbacks

        private void OnPlayButtonClicked() {
            if (!CanPresent() || _isChecking) return;
            if (_isDownloading) {
                ResetDownload();
                return;
            }
            if (_job is { Finished: false }) return;
            if (_replayHeader is { } header) {
                var job = BeginJob();
                if (job == null) return;
                RefreshDownloadButton(DownloadButtonState.Unavailable);
                RefreshPlayButton(PlayButtonState.Unavailable);
                _ = LoadAndStartReplayAsync(job, header).RunCatching();
            } else StartDownload(true);
        }

        private void OnDownloadButtonClicked() {
            if (!CanPresent() || _isChecking) return;
            if (_isDownloading) {
                ResetDownload();
                return;
            }
            if (_job is { Finished: false }) return;
            if (_replayHeader != null) _replayerNavigator!.NavigateToReplayManager(_replayHeader);
            else StartDownload(false);
        }

        #endregion

        #region Other

        private void ResetButtons() {
            RefreshDownloadButton(_isChecking ? DownloadButtonState.Unavailable
                : _replayHeader != null ? DownloadButtonState.ReadyToNavigate : DownloadButtonState.ReadyToDownload);
            RefreshPlayButton(_isChecking ? PlayButtonState.Unavailable : PlayButtonState.ReadyToDownloadOrStart);
        }

        private void ResetDownload() {
            RetireJob();
            if (!CanPresent()) return;
            _downloadText.gameObject.SetActive(false);
            ResetButtons();
        }

        private static string FormatFailString(string? failReason) {
            return $"<color=red>Fail: {failReason}</color>";
        }

        #endregion

        #region Play Button

        private enum PlayButtonState {
            ReadyToDownloadOrStart,
            Downloading,
            Unavailable
        }

        private bool _playCanBeInteractable;

        private void RefreshPlayButton(PlayButtonState state) {
            if (state is PlayButtonState.Unavailable || !_playCanBeInteractable) {
                _playButton.interactable = false;
                return;
            }

            _playButton.interactable = true;
            _playButtonText.text = state switch {
                PlayButtonState.ReadyToDownloadOrStart => "<bll>ls-watch-replay</bll>",
                PlayButtonState.Downloading => "<bll>ls-cancel</bll>",
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
            };
        }

        #endregion

        #region Download Button

        private enum DownloadButtonState {
            ReadyToNavigate,
            ReadyToDownload,
            Downloading,
            Unavailable
        }

        private void RefreshDownloadButton(DownloadButtonState state) {
            var readyToDownload = state is DownloadButtonState.ReadyToDownload;
            var indexing = state is DownloadButtonState.Unavailable;
            
            _downloadButtonText.gameObject.SetActive(!readyToDownload);
            _downloadButtonImage.Enabled = readyToDownload;

            _downloadButtonSpinner.Enabled = indexing;
            _downloadButtonHint.enabled = indexing;
            
            _downloadButton.interactable = state is DownloadButtonState.ReadyToDownload or DownloadButtonState.ReadyToNavigate;

            _downloadButtonText.text = state switch {
                DownloadButtonState.ReadyToNavigate => "\u27a4",
                DownloadButtonState.ReadyToDownload => "",
                DownloadButtonState.Downloading => "<bll>ls-cancel</bll>",
                DownloadButtonState.Unavailable => "",
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
            };
        }

        #endregion

        #region Active

        [UIValue("active"), UsedImplicitly]
        private bool Active {
            get => _active;
            set {
                if (_active.Equals(value)) return;
                _active = value;
                NotifyPropertyChanged();
            }
        }

        private bool _active = true;

        public void SetActive(bool value) {
            var changed = _active != value;
            if (changed && !value) RetireJob();
            Active = value;
            _downloadText.gameObject.SetActive(false);
            if (changed && value) BeginChecking();
        }

        #endregion
    }
}
