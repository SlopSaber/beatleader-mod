using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.API;
using BeatLeader.Manager;
using BeatLeader.Utils;
using UnityEngine;

namespace BeatLeader.DataManager {
    internal class PlaylistsManager : MonoBehaviour {
        private readonly CancellationTokenSource _lifetime = new();
        private bool _destroyed;

        #region Playlists

        private static readonly Dictionary<PlaylistType, PlaylistInfo> Playlists = new() {
            { PlaylistType.Nominated, new PlaylistInfo("nominated", "BeatLeader nominated") },
            { PlaylistType.Qualified, new PlaylistInfo("qualified", "BeatLeader qualified") },
            { PlaylistType.Ranked, new PlaylistInfo("ranked", "BeatLeader ranked") },
        };

        private static bool TryGetPlaylistInfo(PlaylistType playlistType, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PlaylistInfo? playlistInfo) {
            if (!Playlists.ContainsKey(playlistType)) {
                playlistInfo = null;
                return false;
            }

            playlistInfo = Playlists[playlistType];
            return true;
        }

        private class PlaylistInfo {
            public readonly string PlaylistId;
            public readonly string FileName;
            public PlaylistState State = PlaylistState.NotFound;
            public long Revision;
            public bool Updating;

            public PlaylistInfo(string playlistId, string fileName) {
                PlaylistId = playlistId;
                FileName = fileName;
            }
        }

        public enum PlaylistState {
            NotFound,
            Outdated,
            UpToDate
        }

        public enum PlaylistType {
            Nominated,
            Qualified,
            Ranked
        }

        #endregion

        #region PlaylistState

        public static event Action<PlaylistType, PlaylistState> PlaylistStateChangedEvent;

        private static void SetPlaylistState(PlaylistType playlistType, PlaylistState state) {
            if (!TryGetPlaylistInfo(playlistType, out var playlistInfo)) return;
            if (playlistInfo.State == state) return;
            playlistInfo.State = state;
            PlaylistStateChangedEvent?.Invoke(playlistType, state);
        }

        public static PlaylistState GetPlaylistState(PlaylistType playlistType) {
            return !TryGetPlaylistInfo(playlistType, out var playlistInfo) ? PlaylistState.NotFound : playlistInfo.State;
        }

        #endregion

        #region Start

        private void Start() {
            LeaderboardEvents.PlaylistUpdateButtonWasPressedAction += UpdatePlaylist;
            _ = VerifyPlaylistVersion(PlaylistType.Nominated).RunCatching();
            _ = VerifyPlaylistVersion(PlaylistType.Qualified).RunCatching();
            _ = VerifyPlaylistVersion(PlaylistType.Ranked).RunCatching();
        }

        private void OnDestroy() {
            _destroyed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            LeaderboardEvents.PlaylistUpdateButtonWasPressedAction -= UpdatePlaylist;
        }

        private bool IsCurrent(PlaylistInfo info, long revision) {
            return !_destroyed && this && info.Revision == revision;
        }

        #endregion

        #region VerifyPlaylistVersion

        private async Task VerifyPlaylistVersion(PlaylistType playlistType) {
            if (_destroyed || !TryGetPlaylistInfo(playlistType, out var playlistInfo)) return;
            var revision = playlistInfo.Revision;
            var token = _lifetime.Token;
            try {
                var stored = await FileManager.ReadPlaylistAsync(playlistInfo.FileName, token);
                if (!IsCurrent(playlistInfo, revision)) return;
                if (stored == null) {
                    SetPlaylistState(playlistType, PlaylistState.NotFound);
                    return;
                }

                var result = await PlaylistRequest.Send(playlistInfo.PlaylistId, token).Join();
                if (!IsCurrent(playlistInfo, revision)) return;
                if (result.RequestState == WebRequests.RequestState.Finished && result.Result is { } playlistBytes) {
                    var ownedBytes = (byte[])playlistBytes.Clone();
                    var matches = await Task.Run(() => ComparePlaylists(ownedBytes, stored, token), token);
                    if (!IsCurrent(playlistInfo, revision)) return;
                    SetPlaylistState(playlistType, matches ? PlaylistState.UpToDate : PlaylistState.Outdated);
                } else if (result.RequestState is WebRequests.RequestState.Failed or WebRequests.RequestState.Finished) {
                    Plugin.Log.Debug($"{playlistType} playlist check failed: {result.FailReason}");
                }
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            }
        }

        private static bool ComparePlaylists(byte[] a, byte[] b, CancellationToken token) {
            if (a.Length != b.Length) return false;

            for (var i = 0; i < a.Length; i++) {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                if (a[i] != b[i]) return false;
            }

            return true;
        }

        #endregion

        #region UpdatePlaylist

        public static event Action<PlaylistType> PlaylistUpdateStartedEvent;
        public static event Action<PlaylistType> PlaylistUpdateFinishedEvent;

        public async Task UpdatePlaylistAsync(PlaylistType playlistType) {
            if (_destroyed || !TryGetPlaylistInfo(playlistType, out var playlistInfo) || playlistInfo.Updating) return;
            playlistInfo.Updating = true;
            var revision = ++playlistInfo.Revision;
            var token = _lifetime.Token;
            try {
                PlaylistUpdateStartedEvent?.Invoke(playlistType);
                if (!IsCurrent(playlistInfo, revision)) return;
                var result = await PlaylistRequest.Send(playlistInfo.PlaylistId, token).Join();
                if (!IsCurrent(playlistInfo, revision)) return;
                if (result.RequestState == WebRequests.RequestState.Finished && result.Result is { } playlistBytes) {
                    var saved = await FileManager.SavePlaylistAsync(playlistInfo.FileName, playlistBytes, token);
                    if (!IsCurrent(playlistInfo, revision)) return;
                    if (saved) {
                        SongCore.Loader.Instance?.RefreshSongs(false);
                        if (IsCurrent(playlistInfo, revision)) SetPlaylistState(playlistType, PlaylistState.UpToDate);
                    }
                } else if (result.RequestState is WebRequests.RequestState.Failed or WebRequests.RequestState.Finished) {
                    Plugin.Log.Debug($"{playlistType} playlist update failed: {result.FailReason}");
                }
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            } finally {
                playlistInfo.Updating = false;
                if (IsCurrent(playlistInfo, revision)) PlaylistUpdateFinishedEvent?.Invoke(playlistType);
            }
        }

        public void UpdatePlaylist(PlaylistType playlistType) {
            _ = UpdatePlaylistAsync(playlistType).RunCatching();
        }

        #endregion
    }
}
