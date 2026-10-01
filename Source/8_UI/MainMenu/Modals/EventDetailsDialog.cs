using BeatLeader.API;
using BeatLeader.Models;
using BeatLeader.Utils;
using BeatSaberMarkupLanguage.Attributes;
using HMUI;
using JetBrains.Annotations;
using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace BeatLeader.UI.MainMenu {
    internal class EventDetailsDialog : AbstractReeModal<PlatformEvent> {
        private CancellationTokenSource? _downloadCancellation;
        private long _presentation;
        private bool _isOpen;
        private bool _destroyed;

        #region Components

        [UIObject("loading-container"), UsedImplicitly] 
        private GameObject _loadingContainer = null!;

        [UIObject("event-container"), UsedImplicitly] 
        private GameObject _eventContainer = null!;

        [UIComponent("event-description"), UsedImplicitly]
        private TMP_Text _eventDescription = null!;

        private bool _downloadButtonActive;

        [UIValue("download-button-active"), UsedImplicitly]
        private bool DownloadButtonActive {
            get => _downloadButtonActive;
            set {
                if (_downloadButtonActive.Equals(value)) return;
                _downloadButtonActive = value;
                NotifyPropertyChanged();
            }
        }

        #endregion

        #region Events

        protected override void OnContextChanged() {
            CancelDownload();
            _eventDescription.SetText(Context.description ?? "");
            DownloadButtonActive = Context.downloadable;
            if (_isOpen) {
                offClickCloses = true;
                _eventContainer.SetActive(true);
                _loadingContainer.SetActive(false);
            }
        }

        protected override void OnResume() {
            CancelDownload();
            _isOpen = true;
            offClickCloses = true;
            DownloadButtonActive = Context.downloadable;
            _eventContainer.SetActive(true);
            _loadingContainer.SetActive(false);
        }

        protected override void OnPause() {
            _isOpen = false;
            CancelDownload();
            base.OnPause();
        }

        protected override void OnInterrupt() {
            _isOpen = false;
            CancelDownload();
            base.OnInterrupt();
        }

        protected override void OnClose() {
            _isOpen = false;
            CancelDownload();
            base.OnClose();
        }

        protected override void OnDispose() {
            _isOpen = false;
            CancelDownload();
            base.OnDispose();
        }

        protected override void OnDestroy() {
            _destroyed = true;
            _isOpen = false;
            CancelDownload();
            base.OnDestroy();
        }

        private void CancelDownload() {
            _presentation++;
            var source = _downloadCancellation;
            _downloadCancellation = null;
            source?.Cancel();
        }

        private bool IsCurrent(CancellationTokenSource source, long presentation) {
            return CanPresent(presentation) &&
                ReferenceEquals(_downloadCancellation, source) && !source.IsCancellationRequested;
        }

        private bool CanPresent(long presentation) {
            return !_destroyed && _isOpen && _presentation == presentation &&
                this && IsParsed && Content && HasContext;
        }

        private static async Task<bool> RefreshSongsAndWaitAsync(CancellationToken token) {
            var loader = SongCore.Loader.Instance;
            if (loader == null) return true;
            while (SongCore.Loader.AreSongsLoading) {
                await Task.Delay(50, token);
                if (loader == null || loader != SongCore.Loader.Instance) return false;
            }
            token.ThrowIfCancellationRequested();
            loader.RefreshSongs(false);
            while (SongCore.Loader.AreSongsLoading) {
                await Task.Delay(50, token);
                if (loader == null || loader != SongCore.Loader.Instance) return false;
            }
            return loader != null && loader == SongCore.Loader.Instance && SongCore.Loader.AreSongsLoaded;
        }

        private async Task DownloadPlaylist(string playlistId, string eventName, string filename,
            CancellationTokenSource source, long presentation) {
            var token = source.Token;
            try {
                await Task.Yield();
                if (!IsCurrent(source, presentation)) return;
                var result = await PlaylistRequest.Send(playlistId, token).Join();
                if (!IsCurrent(source, presentation)) return;
                if (result.RequestState == WebRequests.RequestState.Finished && result.Result is { } playlistBytes) {
                    var saved = await FileManager.SavePlaylistAsync(filename, playlistBytes, token);
                    if (!IsCurrent(source, presentation)) return;
                    if (saved && await RefreshSongsAndWaitAsync(token)) {
                        if (!IsCurrent(source, presentation)) return;
                        var playlist = await FileManager.FindPlaylistAsync(filename, token);
                        if (!IsCurrent(source, presentation)) return;
                        if (playlist != null) {
                            BeatmapKey beatmapKey = new();
                            var state = new LevelSelectionFlowCoordinator.State(
                                SelectLevelCategoryViewController.LevelCategory.CustomSongs,
                                playlist, in beatmapKey, null);
                            var flow = FindFirstObjectByType<SoloFreePlayFlowCoordinator>();
                            if (flow != null) {
                                flow.Setup(state);
                                if (!IsCurrent(source, presentation)) return;
                                offClickCloses = true;
                                Close();
                                (GameObject.Find("SoloButton") ?? GameObject.Find("Wrapper/BeatmapWithModifiers/BeatmapSelection/EditButton"))
                                    ?.GetComponent<NoTransitionsButton>()?.onClick.Invoke();
                                return;
                            }
                        }
                    }
                    if (!IsCurrent(source, presentation)) return;
                    offClickCloses = true;
                    Close();
                } else {
                    Plugin.Log.Debug($"Event {eventName} playlist update failed: {result.FailReason}");
                    offClickCloses = true;
                    Close();
                }
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            } finally {
                if (ReferenceEquals(_downloadCancellation, source)) {
                    var canPresent = IsCurrent(source, presentation);
                    _downloadCancellation = null;
                    if (canPresent) {
                        offClickCloses = true;
                        _eventContainer.SetActive(true);
                        if (CanPresent(presentation)) _loadingContainer.SetActive(false);
                        if (CanPresent(presentation)) DownloadButtonActive = Context.downloadable;
                    }
                }
                source.Dispose();
            }
        }

        [UIAction("download-button-click"), UsedImplicitly]
        private void HandleDownloadButtonClicked() {
            if (_destroyed || !_isOpen || _downloadCancellation != null || !DownloadButtonActive) return;
            var playlistId = Context.playlistId.ToString();
            var eventName = Context.name;
            var filename = eventName.Replace(" ", "_");
            var source = new CancellationTokenSource();
            _downloadCancellation = source;
            var presentation = ++_presentation;
            DownloadButtonActive = false;
            _eventContainer.SetActive(false);
            _loadingContainer.SetActive(true);
            offClickCloses = false;

            _ = DownloadPlaylist(playlistId, eventName, filename, source, presentation).RunCatching();
        }

        #endregion
    }
}
