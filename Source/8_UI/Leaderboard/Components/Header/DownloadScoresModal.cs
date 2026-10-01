using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.API;
using BeatLeader.Models;
using BeatLeader.Models.Replay;
using BeatLeader.UI;
using BeatLeader.UI.Reactive.Components;
using BeatLeader.Utils;
using BeatLeader.WebRequests;
using Reactive;
using Reactive.BeatSaber.Components;
using Reactive.Components;
using Reactive.Yoga;
using TMPro;
using UnityEngine;

namespace BeatLeader.Components {
    internal class DownloadScoresModal : DialogBase {
        #region Download

        public IReadOnlyCollection<IReplayHeader> Headers => _headers;
        public Action? DownloadingFinishedCallback { get; set; }

        private IReadOnlyCollection<IReplayHeader> _headers = Array.Empty<IReplayHeader>();
        private Score[]? _scores;
        private DownloadJob? _job;
        private long _revision;
        private bool _destroyed;

        private sealed class ScoreSnapshot : IReplayHashProvider {
            public long Timestamp { get; }
            public string PlayerID { get; }
            public string ReplayUrl { get; }

            public ScoreSnapshot(Score score) {
                var provider = (IReplayHashProvider)score;
                Timestamp = provider.Timestamp;
                PlayerID = provider.PlayerID;
                ReplayUrl = score.replay;
            }
        }

        private sealed class DownloadJob {
            public readonly Score[] Scores;
            public readonly bool SaveReplays;
            public readonly long Revision;
            public readonly CancellationTokenSource Cancellation = new();
            public readonly CancellationToken Token;
            public bool Finished;

            public DownloadJob(Score[] scores, bool saveReplays, long revision) {
                Scores = scores;
                SaveReplays = saveReplays;
                Revision = revision;
                Token = Cancellation.Token;
            }
        }

        private int _totalRequests;
        private int _currentRequests;
        private bool _saveReplays;

        public void SetData(IReadOnlyCollection<Score> scores) {
            CancelDownloading();
            _scores = scores.ToArray();
            _headers = Array.Empty<IReplayHeader>();
            _downloadingWasEverStarted = false;
            if (CanPresent()) BeginChecking();
        }

        private bool CanPresent() {
            return !_destroyed && IsInitialized && !IsDestroyed && IsOpened && Content;
        }

        private bool IsCurrent(DownloadJob job) {
            return ReferenceEquals(_job, job) && _revision == job.Revision && !job.Token.IsCancellationRequested && CanPresent();
        }

        private DownloadJob? BeginJob() {
            if (_scores == null) {
                Plugin.Log.Error("Scores are null, nothing to download/check!");
                return null;
            }
            CancelDownloading();
            var job = new DownloadJob(_scores, _saveReplays, _revision);
            _job = job;
            _headers = Array.Empty<IReplayHeader>();
            return job;
        }

        private void CancelDownloading() {
            var job = _job;
            _job = null;
            _revision++;
            if (job is { Finished: false }) job.Cancellation.Cancel();
        }

        private void FailDownloading(DownloadJob job, string reason) {
            if (!IsCurrent(job)) return;
            job.Cancellation.Cancel();
            SetPanicking(reason);
        }

        private void StartDownloading() {
            var job = BeginJob();
            if (job == null) return;
            _totalRequests = _currentRequests = 0;
            SetDownloading();
            RefreshDownloading();
            _ = StartDownloadingInternal(job).RunCatching();
        }

        private async Task StartDownloadingInternal(DownloadJob job) {
            var token = job.Token;
            try {
                await Task.Yield();
                if (!IsCurrent(job)) return;
                var scores = job.Scores.Select(static score => new ScoreSnapshot(score)).ToArray();
                if (!IsCurrent(job)) return;
                ReplayManager.StartLoadingIfNeverLoaded();
                var existing = await ReplayManager.FindReplaysByHashAsync(scores, token);
                if (!IsCurrent(job)) return;
                var headers = new List<IReplayHeader>(existing.Present);
                var requests = new List<Task<IWebRequest<Replay>>>();
                for (var i = 0; i < scores.Length; i++) {
                    if (!IsCurrent(job)) return;
                    if (existing.Headers[i] == null) requests.Add(DownloadReplayRequest.SendRequest(scores[i].ReplayUrl, token).Join());
                }
                if (!IsCurrent(job)) return;
                _totalRequests = requests.Count;
                _currentRequests = 0;
                RefreshDownloading();
                var replays = new List<Replay>();
                foreach (var request in requests) {
                    var result = await request;
                    if (!IsCurrent(job)) return;
                    if (result.RequestStatusCode != HttpStatusCode.OK || result.Result is not { } replay) {
                        FailDownloading(job, result.FailReason ?? "Failed to download the replay");
                        return;
                    }
                    replays.Add(replay);
                    _currentRequests++;
                    RefreshDownloading();
                }
                if (!IsCurrent(job)) return;
                SetSaving();
                foreach (var replay in replays) {
                    if (!IsCurrent(job)) return;
                    IReplayHeader? header;
                    if (job.SaveReplays) {
                        var result = await ReplayManager.SaveAnyReplayAsync(replay, null, token);
                        if (!IsCurrent(job)) return;
                        if (result.Error is ReplaySavingError.AlreadyExists) {
                            header = ReplayManager.FindReplayByHash(replay.info);
                            Plugin.Log.Error("[ReplayManager] Hash collision occured! The replay won't be saved");
                            if (header != null) Plugin.Log.Error($"Collision | player: {header.ReplayInfo.PlayerID} | timestamp: {header.ReplayInfo.Timestamp}");
                        } else header = result.Header;
                    } else header = ReplayManager.CreateTempReplayHeader(replay, null);
                    if (header == null) {
                        FailDownloading(job, "Failed to save the replay");
                        return;
                    }
                    headers.Add(header);
                }
                if (!IsCurrent(job)) return;
                var ready = await Task.Run(() => Array.AsReadOnly(headers.ToArray()), token);
                if (!IsCurrent(job)) return;
                _headers = ready;
                SetDownloadingFinished();
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            } catch (Exception error) {
                Plugin.Log.Error(error);
                FailDownloading(job, error.Message);
            } finally {
                job.Finished = true;
                job.Cancellation.Dispose();
            }
        }

        #endregion

        #region Check Downloaded

        private void BeginChecking() {
            var job = BeginJob();
            if (job == null) return;
            SetChecking();
            _ = CheckReplaysDownloaded(job).RunCatching();
        }

        private async Task CheckReplaysDownloaded(DownloadJob job) {
            var token = job.Token;
            try {
                await Task.Yield();
                if (!IsCurrent(job)) return;
                var scores = job.Scores.Select(static score => new ScoreSnapshot(score)).ToArray();
                if (!IsCurrent(job)) return;
                ReplayManager.StartLoadingIfNeverLoaded();
                var found = await ReplayManager.FindReplaysByHashAsync(scores, token);
                if (!IsCurrent(job)) return;
                if (found.Complete) {
                    _headers = found.Present;
                    SetInitiallyReady();
                } else SetWaitingForStart();
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            } catch (Exception error) {
                Plugin.Log.Error(error);
                FailDownloading(job, error.Message);
            } finally {
                job.Finished = true;
                job.Cancellation.Dispose();
            }
        }

        #endregion

        #region Construct

        private Label _progressLabel = null!;
        private ProgressBar _progressBar = null!;
        private Toggle _saveReplaysToggle = null!;
        private IReactiveComponent _saveReplaysContainer = null!;

        protected override ILayoutItem ConstructContent() {
            return new Layout {
                Children = {
                    new Label {
                        Overflow = TextOverflowModes.Ellipsis
                    }.AsFlexItem(alignSelf: Align.Center).Bind(ref _progressLabel),

                    new Layout {
                        Children = {
                            new Toggle()
                                .WithListener(x => x.Active, x => _saveReplays = x)
                                .Bind(ref _saveReplaysToggle)
                                .InNamedRail("Save replays?")
                        }
                    }.AsFlexGroup(
                        direction: FlexDirection.Column,
                        padding: new() { bottom = 1f },
                        gap: new() { y = 1f }
                    ).AsFlexItem().Bind(ref _saveReplaysContainer),

                    new ProgressBar()
                        .AsFlexItem(margin: new() { left = 2f, right = 2f })
                        .Bind(ref _progressBar)
                }
            }.AsFlexGroup(
                direction: FlexDirection.Column,
                justifyContent: Justify.Center,
                alignItems: Align.Stretch,
                padding: new() { left = 3f, right = 3f },
                gap: new() { y = 1f }
            );
        }

        protected override void OnInitialize() {
            OkButtonInteractable = false;
            OkButtonText = "Proceed";
            Title = "Download Scores";

            Content.GetOrAddComponent<CanvasGroup>().ignoreParentGroups = true;

            if (LayoutController is YogaLayoutController yoga) {
                yoga.ConstrainVertical = false;
                yoga.ConstrainHorizontal = false;
            }
            
            this.AsFlexItem(minSize: new() { x = 52.pt(), y = 30.pt() });
            base.OnInitialize();
        }

        protected override void OnOpen(bool opened) {
            if (opened) {
                return;
            }

            _saveReplaysToggle.SetActive(false, false);
            _saveReplays = false;
            _downloadingWasEverStarted = false;
            BeginChecking();
        }

        protected override void OnClose(bool closed) {
            CancelDownloading();
            _downloadingWasEverStarted = false;
        }

        protected override void OnDestroy() {
            _destroyed = true;
            CancelDownloading();
            DownloadingFinishedCallback = null;
            _headers = Array.Empty<IReplayHeader>();
            _scores = null;
            base.OnDestroy();
        }

        protected override void OnOkButtonClicked() {
            if (!CanPresent() || _job is not { Finished: true } job || !IsCurrent(job)) return;
            if (!_downloadingWasEverStarted) {
                StartDownloading();
                return;
            }

            DownloadingFinishedCallback?.Invoke();
            if (IsCurrent(job)) CloseInternal();
        }

        protected override void OnCancelButtonClicked() {
            if (!CanPresent()) return;
            CancelDownloading();
            CloseInternal();
        }

        #endregion

        #region UI

        private bool _downloadingWasEverStarted;

        private void RefreshDownloading() {
            _progressBar.TotalProgress = _totalRequests;
            _progressBar.Progress = _currentRequests;
            _progressLabel.Text = $"Downloading {_currentRequests}/{_totalRequests}";
        }

        private void SetDownloading() {
            _saveReplaysContainer.Enabled = false;
            _progressBar.Enabled = true;
            _downloadingWasEverStarted = true;
        }

        private void SetChecking() {
            _progressLabel.Text = "Wait just a little bit...";
            _progressLabel.Color = Color.white;
            CancelButtonInteractable = true;
            OkButtonInteractable = false;
            ShowOkButton = true;
        }

        private void SetWaitingForStart() {
            _progressLabel.Color = Color.white;
            _progressLabel.Text = "Let's clarify before we start";
            _progressBar.Color = UIStyle.ControlButtonColorSet.ActiveColor;
            _progressBar.Progress = 0f;
            _progressBar.Enabled = false;
            _saveReplaysContainer.Enabled = true;

            CancelButtonInteractable = true;
            OkButtonInteractable = true;
            ShowOkButton = true;
        }

        private void SetPanicking(string reason) {
            _progressLabel.Text = $"{reason}";
            _progressLabel.Color = Color.red;
            _progressBar.Color = Color.red;
            OkButtonInteractable = false;
            ShowOkButton = false;
        }

        private void SetDownloadingFinished() {
            _progressLabel.Text = "Downloading finished";
            _progressBar.Progress = 1f;
            _progressBar.TotalProgress = 1f;
            _progressBar.Color = Color.green * 0.8f;
            OkButtonInteractable = true;
        }

        private void SetInitiallyReady() {
            _progressLabel.Text = "Everything is ready!";
            _progressBar.Enabled = false;
            _saveReplaysContainer.Enabled = false;
            CancelButtonInteractable = true;
            OkButtonInteractable = true;
            _downloadingWasEverStarted = true;
        }

        private void SetSaving() {
            _progressLabel.Text = "Saving replays...";
            OkButtonInteractable = false;
        }

        #endregion
    }
}
