using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.DataManager;
using BeatLeader.Replayer.Emulation;
using BeatLeader.Interop;
using BeatLeader.Models;
using BeatLeader.Models.AbstractReplay;
using BeatLeader.Models.Replay;
using BeatLeader.UI.Hub;
using BeatLeader.Utils;
using BeatSaber.BeatAvatarSDK;
using JetBrains.Annotations;
using ModestTree;
using SiraUtil.Tools.FPFC;
using UnityEngine;
using Zenject;

namespace BeatLeader.Replayer {
    [PublicAPI]
    public class ReplayerMenuLoader : MonoBehaviour {
        #region Injection

        [Inject] private readonly ReplayerLauncher _launcher = null!;
        [Inject] private readonly BeatAvatarLoader _avatarLoader = null!;
        [Inject] private readonly GameScenesManager _scenesManager = null!;
        [Inject] private readonly IFPFCSettings _fpfcSettings = null!;
        [Inject] private readonly BeatmapLevelsModel _levelsModel = null!;

        #endregion

        #region Init

        public static ReplayerMenuLoader? Instance { get; private set; }

        private void Awake() {
            if (Instance is not null) {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        #endregion

        #region StartReplayFromLeaderboard

        internal readonly struct LeaderboardReplaySelection {
            public readonly string Hash;
            public readonly BeatmapLevelWithKey Beatmap;

            private LeaderboardReplaySelection(string hash, BeatmapLevelWithKey beatmap) {
                Hash = hash;
                Beatmap = beatmap;
            }

            public static LeaderboardReplaySelection Capture() {
                return new(LeaderboardState.SelectedLeaderboardKey.Hash,
                    new(LeaderboardState.SelectedBeatmapLevel, LeaderboardState.SelectedBeatmapKey));
            }
        }

        internal Task StartReplayFromLeaderboardAsync(Replay replay, Player player, Action? finishCallback = null) {
            return StartReplayFromLeaderboardAsync(replay, player, CancellationToken.None, finishCallback: finishCallback);
        }

        internal async Task StartReplayFromLeaderboardAsync(
            Replay replay, Player player, CancellationToken token,
            LeaderboardReplaySelection? selection = null, Action? finishCallback = null
        ) {
            token.ThrowIfCancellationRequested();
            var selected = selection ?? LeaderboardReplaySelection.Capture();
            var info = replay.info;
            var beatmapHash = info.hash;
            //try to load the beatmap, first attempt with replay hash, then with leaderboard hash if it fails
            var beatmap = await LoadBeatmapForLeaderboardAsync(beatmapHash, info.mode, info.difficulty, selected.Hash, token);
            token.ThrowIfCancellationRequested();
            if (this == null) return;
            //if the beatmap still fails to load, force load the selected beatmap
            if (!beatmap.HasValue) {
                Plugin.Log.Warn("Beatmap load failed after two attempts; forcing selected beatmap load...");
                beatmap = selected.Beatmap;
            }
            var optionalData = await LoadOptionalDataAsync(replay.info, player, token);
            token.ThrowIfCancellationRequested();
            if (this == null) return;
            //start the replay
            await StartReplayer(
                beatmap,
                replay,
                player,
                optionalData,
                ReplayerSettings.UserSettings,
                finishCallback,
                token
            );
        }

        private async Task<BeatmapLevelWithKey> LoadBeatmapForLeaderboardAsync(
            string beatmapHash, string mode, string difficulty, string selectedHash, CancellationToken token
        ) {
            var beatmap = await LoadBeatmapAsync(beatmapHash, mode, difficulty, token);
            token.ThrowIfCancellationRequested();
            if (this == null) return default;
            //if fails try to load with selected beatmap hash
            if (!beatmap.HasValue) {
                Plugin.Log.Warn("Failed to load the map by hash; attempting to use leaderboard hash...");
                beatmap = await LoadBeatmapAsync(selectedHash, mode, difficulty, token);
            }
            //
            return beatmap;
        }

        #endregion

        #region StartReplay

        public async Task<bool> StartReplayAsync(
            Replay replay,
            IPlayer? player = null,
            BattleRoyaleReplayData? optionalData = null,
            ReplayerSettings? settings = null,
            Action? finishCallback = null,
            CancellationToken token = default
        ) {
            settings ??= ReplayerSettings.UserSettings;

            var info = replay.info;
            var beatmap = await LoadBeatmapAsync(info.hash, info.mode, info.difficulty, token);

            if (!beatmap.HasValue) {
                return false;
            }

            if (!optionalData.HasValue) {
                var data = await LoadOptionalDataAsync(replay.info, player, token);
                optionalData = data;
            }

            await StartReplayer(
                beatmap,
                replay,
                player,
                optionalData.Value,
                settings,
                finishCallback,
                token
            );
            return true;
        }

        public async Task<bool> StartBattleRoyaleAsync(
            IReadOnlyCollection<IBattleRoyaleReplay> replays,
            ReplayerSettings? settings = null,
            Action? finishCallback = null,
            CancellationToken token = default
        ) {
            if (replays.Count == 0) {
                return false;
            }

            settings ??= ReplayerSettings.UserSettings;

            var info = replays.First().ReplayHeader.ReplayInfo;
            var beatmap = await LoadBeatmapAsync(info.SongHash, info.SongMode, info.SongDifficulty, token);

            if (!beatmap.HasValue) {
                return false;
            }

            var replayDatas = new List<ReplayData>();
            var tasks = replays.Select(x => LoadAndAppendReplay(x, replayDatas));

            // wait for all tasks and return if at least one of them failed
            foreach (var task in tasks) {
                if (!await task) {
                    return false;
                }
            }

            await StartReplayer(beatmap, replayDatas, settings, finishCallback, token);
            return true;
        }

        private static async Task<bool> LoadAndAppendReplay(IBattleRoyaleReplay royaleReplay, ICollection<ReplayData> collection) {
            var header = royaleReplay.ReplayHeader;

            var replay = await header.LoadReplayAsync(CancellationToken.None);

            if (replay == null) {
                return false;
            }

            var player = await header.LoadPlayerAsync(false, CancellationToken.None);

            var data = new ReplayData {
                replay = replay,
                player = player,
                optionalData = await LoadOptionalDataAsync(replay.info, player)
            };

            collection.Add(data);
            return true;
        }

        #endregion

        #region StartReplayer

        private struct ReplayData {
            public Replay replay;
            public IPlayer? player;
            public BattleRoyaleReplayData? optionalData;
        }

        private async Task StartReplayer(
            BeatmapLevelWithKey beatmap,
            Replay replay,
            IPlayer? player,
            BattleRoyaleReplayData? optionalData,
            ReplayerSettings settings,
            Action? finishCallback,
            CancellationToken token
        ) {
            await StartReplayer(
                beatmap,
                new ReplayData {
                    replay = replay,
                    player = player,
                    optionalData = optionalData
                }.Yield().ToArray(),
                settings,
                finishCallback,
                token
            );
        }

        private async Task StartReplayer(
            BeatmapLevelWithKey beatmap,
            IReadOnlyCollection<ReplayData> replays,
            ReplayerSettings settings,
            Action? finishCallback,
            CancellationToken token
        ) {
            var data = new ReplayLaunchData();
            var list = new List<IReplay>();

            token.ThrowIfCancellationRequested();
            if (this == null) return;
            var shouldMirror = !replays.All(r => r.replay.info.leftHanded);
            var loadPlayerEnvironment = settings.LoadPlayerEnvironment;

            //loading replays
            foreach (var replayData in replays) {
                //adding extra info
                var info = replayData.replay.info;
                Plugin.Log.Info("Attempting to load replay:\r\n" + info);
                ReplayManager.SaturateReplayInfo(info, null);
                
                //loading environment
                if (loadPlayerEnvironment) {
                    LoadEnvironment(data, info.environment);
                }

                //converting
                var creplay = await ReplayDataUtils.ConvertToAbstractReplayAsync(
                    replayData.replay,
                    replayData.player,
                    replayData.optionalData,
                    shouldMirror && info.leftHanded,
                    token
                );
                token.ThrowIfCancellationRequested();
                if (this == null) return;
                list.Add(creplay);
            }
            //initializing data
            data.Init(
                list,
                settings,
                beatmap,
                data.EnvironmentInfo
            );
            //starting
            token.ThrowIfCancellationRequested();
            await StartReplayerAsync(data, finishCallback, token);
        }

        public void StartReplayer(ReplayLaunchData data, Action? finishCallback) {
            _ = StartReplayerAsync(data, finishCallback).RunCatching();
        }

        public async Task StartReplayerAsync(ReplayLaunchData data, Action? finishCallback, CancellationToken token = default) {
            await _avatarLoader.CreateEditorFlowCoordinator();
            token.ThrowIfCancellationRequested();
            if (this == null) return;
            data.ReplayWasFinishedEvent += HandleReplayWasFinished;
            if (!_launcher.StartReplay(data, finishCallback)) {
                data.ReplayWasFinishedEvent -= HandleReplayWasFinished;
                return;
            }
            InputUtils.OverrideUsesFPFC = InputUtils.HasFpfcArg && _fpfcSettings.Ignore ? _fpfcSettings.Enabled : null;
        }

        public async Task StartLastReplayAsync() {
            if (Instance == null || ReplayManager.LastPlayedReplay is not { } header) {
                return;
            }

            var replay = await header.LoadReplayAsync(CancellationToken.None);

            await StartReplayAsync(replay!, ProfileManager.Profile);
        }

        private void HandleReplayWasFinished(StandardLevelScenesTransitionSetupDataSO transitionData, ReplayLaunchData launchData) {
            Plugin.Log.Notice("[Replayer] Popping replay scenes");
            launchData.ReplayWasFinishedEvent -= HandleReplayWasFinished;
            _scenesManager.PopScenes(0.3f);

            _fpfcSettings.Enabled = InputUtils.OverrideUsesFPFC ?? InputUtils.HasFpfcArg;
            InputUtils.OverrideUsesFPFC = null;
            InputUtils.EnableCursor(!InputUtils.HasFpfcArg);
        }

        #endregion

        #region ReplayTools

        private static BeatmapLevelWithKey _cachedBeatmap;
        private string? _cachedBeatmapHash;
        private string? _cachedBeatmapCharacteristic;

        public async Task<bool> CanLaunchReplay(ReplayInfo info) {
            return await LoadBeatmapAsync(
                info.hash,
                info.mode,
                info.difficulty,
                CancellationToken.None
            ) is var beatmap && SongCoreInterop.ValidateRequirements(beatmap);
        }

        public async Task<bool> LoadBeatmapAsync(
            ReplayLaunchData launchData,
            string hash,
            string mode,
            string difficulty,
            CancellationToken token
        ) {
            var beatmap = await LoadBeatmapAsync(hash, mode, difficulty, token);
            if (!beatmap.HasValue) {
                return false;
            }

            launchData.BeatmapLevel = beatmap;
            return true;
        }

        public bool LoadEnvironment(ReplayLaunchData launchData, string environmentName) {
            if (environmentName == "Multiplayer") {
                Plugin.Log.Notice("[ReplayerLoader] Map was played in MP. Skipping \"Multiplayer\" environment");
                return false;
            }

            var environment = Resources.FindObjectsOfTypeAll<EnvironmentInfoSO>()
                .FirstOrDefault(x => x.environmentName == environmentName);

            if (environment == null) {
                Plugin.Log.Error("[ReplayerLoader] Failed to load specified environment");
                return false;
            }

            Plugin.Log.Notice("[ReplayerLoader] Applied specified environment: " + environmentName);

            launchData.EnvironmentInfo = environment;
            return true;
        }

        public async Task<BeatmapLevelWithKey> LoadBeatmapAsync(
            string hash,
            string mode,
            string difficulty,
            CancellationToken token
        ) {
            if (!Enum.TryParse(difficulty, out BeatmapDifficulty cdifficulty)) {
                return default;
            }

            if (_cachedBeatmap is { HasValue: true }
                && _cachedBeatmapHash == hash
                && _cachedBeatmapCharacteristic == mode
                && _cachedBeatmap.Key.difficulty == cdifficulty
               ) {
                return _cachedBeatmap;
            }

            var beatmapLevel = await GetBeatmapLevelByHashAsync(hash, token);
            if (beatmapLevel == null) return default;

            var characteristic = beatmapLevel.GetCharacteristics()
                .FirstOrDefault(x => x.SerializedName() == mode);
            if (token.IsCancellationRequested) return default;

            var beatmapKey = beatmapLevel.GetBeatmapKeys()
                .FirstOrDefault(k => k.characteristic == characteristic && k.difficulty == cdifficulty);
            if (beatmapKey == null || token.IsCancellationRequested) return default;

            _cachedBeatmap = new(beatmapLevel, beatmapKey);
            _cachedBeatmapHash = hash;
            _cachedBeatmapCharacteristic = mode;
            return _cachedBeatmap;
        }

        private sealed class BeatmapHashRequest {
            private readonly string[] _keys;
            private readonly string _hash;
            private readonly CompareInfo _comparison;
            private readonly CancellationToken _token;

            public BeatmapHashRequest(string[] keys, string hash, CompareInfo comparison, CancellationToken token) {
                _keys = keys;
                _hash = hash;
                _comparison = comparison;
                _token = token;
            }

            public string? FindPrefix() {
                foreach (var key in _keys) {
                    if (_token.IsCancellationRequested) return null;
                    if (_comparison.IsPrefix(key, _hash, CompareOptions.None)) return key;
                }
                return null;
            }
        }

        public async Task<BeatmapLevel?> GetBeatmapLevelByHashAsync(string hash, CancellationToken token) {
            if (token.IsCancellationRequested || this == null) return null;
            if (hash.Length == 40) {
                var request = new BeatmapHashRequest(
                    _levelsModel._allLoadedBeatmapLevelsRepository._idToBeatmapLevel.Keys.ToArray(),
                    hash, CultureInfo.CurrentCulture.CompareInfo, token);
                var lookup = Task.Run(request.FindPrefix);
                var fixedHash = await lookup;
                if (token.IsCancellationRequested || this == null) return null;

                if (fixedHash != null) {
                    hash = fixedHash;
                }
            }

            if (await _levelsModel.CheckBeatmapLevelDataExistsAsync(hash, BeatmapLevelDataVersion.Original, token)) {
                if (token.IsCancellationRequested || this == null) return null;
                return _levelsModel.GetBeatmapLevel(hash);
            }
            if (token.IsCancellationRequested || this == null) return null;
            if (await _levelsModel.CheckBeatmapLevelDataExistsAsync(CustomLevelLoader.kCustomLevelPrefixId + hash, BeatmapLevelDataVersion.Original, token)) {
                if (token.IsCancellationRequested || this == null) return null;
                return _levelsModel.GetBeatmapLevel(CustomLevelLoader.kCustomLevelPrefixId + hash);
            }

            return null;
        }

        #endregion

        #region Static Tools

        private static async Task<BattleRoyaleReplayData> LoadOptionalDataAsync(
            IReplayInfo? replayInfo, IPlayer? player, CancellationToken token = default
        ) {
            token.ThrowIfCancellationRequested();
            Color? accentColor = null;
            AvatarData? avatarData = null;

            if (player != null) {
                avatarData = await player.GetBeatAvatarAsync(false, token);
                token.ThrowIfCancellationRequested();
            }

            if (replayInfo != null) {
                var colorSeed = $"{replayInfo.Timestamp}{replayInfo.PlayerID}{replayInfo.SongName}".GetHashCode();
                accentColor = ColorUtils.RandomColor(rand: new(colorSeed));
            }

            return new BattleRoyaleReplayData(avatarData, accentColor);
        }

        #endregion
    }
}
