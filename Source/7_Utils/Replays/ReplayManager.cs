using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Models;
using BeatLeader.Models.Replay;
using JetBrains.Annotations;
using UnityEngine.Scripting;

namespace BeatLeader.Utils {
    /// <summary>
    /// A class for managing physical replays.
    /// </summary>
    [PublicAPI]
    public static class ReplayManager {
        public const string ReplayFileExtension = ".bsor";
        internal static readonly string LegacyCacheDirectory = Path.Combine(IPA.Utilities.UnityGame.UserDataPath, "BeatLeader", "ReplayerCache\\");
        private const string ReplayFilePattern = "*.bsor";

        #region Events

        public static event Action<IReplayHeader>? ReplayAddedEvent;
        public static event Action<IReplayHeader>? ReplayDeletedEvent;
        public static event Action? AllReplaysDeletedEvent;

        public static event Action? LoadingStartedEvent;

        /// <summary>
        /// True if loading has finished. False if was cancelled.
        /// </summary>
        public static event Action<bool>? LoadingFinishedEvent;

        private static int _lastBatchIndex;
        private static long _loadHeadersVersion;
        private static SynchronizationContext? _mainThreadSynchronizationContext;

        /// <summary>
        /// Ensures that invocations always happen on the main thread and do not overlap.
        /// </summary>
        private static void SyncNotifyReplaysAdded(CancellationToken token, long version) {
            if (_mainThreadSynchronizationContext == null) {
                Plugin.Log.Error("Failed to invoke ReplayAddedEvent because SynchronizationContext was null");
                return;
            }

            _mainThreadSynchronizationContext.Post(
                static state => {
                    var (notificationToken, notificationVersion) = ((CancellationToken, long))state;
                    IReplayHeader[] batch;
                    int count;
                    lock (headersLocker) {
                        if (notificationToken.IsCancellationRequested || notificationVersion != Volatile.Read(ref _loadHeadersVersion)) {
                            return;
                        }
                        count = headers.Count;
                        if (_lastBatchIndex >= count) {
                            _lastBatchIndex = count;
                            return;
                        }
                        batch = new IReplayHeader[count - _lastBatchIndex];
                        headers.CopyTo(_lastBatchIndex, batch, 0, batch.Length);
                    }
                    foreach (var header in batch) {
                        if (notificationToken.IsCancellationRequested || notificationVersion != Volatile.Read(ref _loadHeadersVersion)) {
                            return;
                        }
                        ReplayAddedEvent?.Invoke(header);
                    }
                    lock (headersLocker) {
                        if (!notificationToken.IsCancellationRequested && notificationVersion == Volatile.Read(ref _loadHeadersVersion)) {
                            _lastBatchIndex = count;
                        }
                    }
                },
                (token, version)
            );
        }

        #endregion

        #region Replays Loading

        /// <summary>
        /// All loaded replays.
        /// </summary>
        public static IReadOnlyList<IReplayHeader> Headers => headers;

        /// <summary>
        /// Are headers being loaded or not.
        /// </summary>
        public static bool IsLoading => _loadHeadersTask != null;

        private static CancellationTokenSource _loadHeadersCancellationSource = new();
        private static Task? _loadHeadersTask;
        private static bool _everLoaded;

        /// <summary>
        /// Starts headers loading if never loaded before.
        /// </summary>
        public static bool StartLoadingIfNeverLoaded() {
            if (_everLoaded) {
                return false;
            }

            StartLoading();
            _everLoaded = true;

            return true;
        }

        /// <summary>
        /// Starts headers loading.
        /// </summary>
        public static void StartLoading() {
            if (Thread.CurrentThread.ManagedThreadId != 1) {
                throw new InvalidOperationException("StartLoading must be called from the main thread");
            }

            if (_loadHeadersTask != null) {
                _loadHeadersCancellationSource.Cancel();
                _loadHeadersCancellationSource = new CancellationTokenSource();
            }

            _mainThreadSynchronizationContext = SynchronizationContext.Current;
            var version = Interlocked.Increment(ref _loadHeadersVersion);
            _loadHeadersTask = LoadReplayHeadersAsync(_loadHeadersCancellationSource.Token, version).RunCatching();
            LoadingStartedEvent?.Invoke();
        }

        /// <summary>
        /// Cancels headers loading.
        /// </summary>
        /// <param name="resetLoadedHeaders">Determines if loaded headers should be reset or not.</param>
        public static void CancelLoading(bool resetLoadedHeaders = true) {
            _loadHeadersCancellationSource.Cancel();
            _loadHeadersCancellationSource = new CancellationTokenSource();
            Interlocked.Increment(ref _loadHeadersVersion);

            _loadHeadersTask = null;

            if (resetLoadedHeaders) {
                lock (headersLocker) {
                    headers.Clear();
                    hashedHeaders.Clear();
                    _lastBatchIndex = 0;
                }
            }

            LoadingFinishedEvent?.Invoke(false);
        }

        /// <summary>
        /// Suspends an execution until headers loading is finished.
        /// </summary>
        public static Task WaitForLoadingAsync() {
            return _loadHeadersTask ?? Task.CompletedTask;
        }

        internal static async Task WaitForLoadingAsync(CancellationToken token) {
            token.ThrowIfCancellationRequested();
            var loading = WaitForLoadingAsync();
            if (loading.IsCompleted) {
                await loading;
                token.ThrowIfCancellationRequested();
                return;
            }
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(), cancelled)) {
                await await Task.WhenAny(loading, cancelled.Task);
                token.ThrowIfCancellationRequested();
            }
        }

        private readonly struct ReplayHashSnapshot : IReplayHashProvider {
            public long Timestamp { get; }
            public string PlayerID { get; }

            public ReplayHashSnapshot(long timestamp, string playerId) {
                Timestamp = timestamp;
                PlayerID = playerId;
            }
        }

        internal static async Task<(IReplayHeader?[] Headers, IReadOnlyCollection<IReplayHeader> Present, bool Complete)> FindReplaysByHashAsync(
            IReadOnlyList<IReplayHashProvider> providers, CancellationToken token) {
            var keys = new ReplayHashSnapshot[providers.Count];
            for (var i = 0; i < keys.Length; i++) {
                token.ThrowIfCancellationRequested();
                var provider = providers[i];
                keys[i] = new ReplayHashSnapshot(provider.Timestamp, provider.PlayerID);
            }
            while (true) {
                await WaitForLoadingAsync(token);
                await replayOperations.WaitAsync(token);
                try {
                    if (IsLoading) continue;
                    var version = Volatile.Read(ref _loadHeadersVersion);
                    var results = await Task.Run(() => {
                        var found = new IReplayHeader?[keys.Length];
                        var present = new List<IReplayHeader>();
                        for (var i = 0; i < keys.Length; i++) {
                            token.ThrowIfCancellationRequested();
                            hashedHeaders.TryGetValue(keys[i].CalculateReplayHash(), out found[i]);
                            if (found[i] is { } header) present.Add(header);
                        }
                        return (Headers: found, Present: (IReadOnlyCollection<IReplayHeader>)Array.AsReadOnly(present.ToArray()), Complete: present.Count == keys.Length);
                    }, token);
                    token.ThrowIfCancellationRequested();
                    if (version == Volatile.Read(ref _loadHeadersVersion)) return results;
                } finally {
                    replayOperations.Release();
                }
            }
        }

        /// <summary>
        /// Finds a header by the specified info if the header is present in the local storage.
        /// </summary>
        /// <param name="info">An info to calculate the hash from.</param>
        /// <returns>A header for the specified info if present.</returns>
        public static IReplayHeader? FindReplayByHash(IReplayHashProvider info) {
            var hash = info.CalculateReplayHash();

            hashedHeaders.TryGetValue(hash, out var header);

            return header;
        }

        #endregion

        #region Replays Loading Logic

        private static readonly ConcurrentDictionary<int, IReplayHeader> hashedHeaders = new();
        private static readonly List<IReplayHeader> headers = new();
        private static readonly object headersLocker = new();
        // Replay reads, saves, scans and deletes await admission; callbacks can enqueue without blocking the owner.
        private static readonly SemaphoreSlim replayOperations = new(1, 1);

        private static async Task LoadReplayHeadersAsync(CancellationToken token, long version) {
            await Task.Yield();
            var stopwatch = new Stopwatch();
            try {
                await replayOperations.WaitAsync(token);
                try {
                    token.ThrowIfCancellationRequested();
                    if (version != Volatile.Read(ref _loadHeadersVersion)) return;

                    if (headers.Count != 0) {
                        AllReplaysDeletedEvent?.Invoke();
                    }
                    token.ThrowIfCancellationRequested();
                    lock (headersLocker) {
                        headers.Clear();
                        hashedHeaders.Clear();
                        _lastBatchIndex = 0;
                    }
                    stopwatch.Start();

                    await ReplayHeadersCache.WaitForLoading();
                    token.ThrowIfCancellationRequested();
                    var queue = await Task.Run(
                        () => new ConcurrentQueue<string>(FileManager.GetAllReplayPaths()),
                        token
                    );
                    token.ThrowIfCancellationRequested();

                    var worker = () => {
                        while (queue.TryDequeue(out var path)) {
                            token.ThrowIfCancellationRequested();
                            try {
                                LoadReplayHeader(path, token, version);
                            } catch (Exception ex) {
                                Plugin.Log.Error($"Failed to load {path}: {ex}");
                            }
                        }
                    };

                    var workerTasks = Enumerable.Range(0, 8).Select(_ => Task.Run(worker, token)).ToArray();
                    await Task.WhenAll(workerTasks);
                    token.ThrowIfCancellationRequested();
                    ReplayHeadersCache.SaveCache();
                } finally {
                    replayOperations.Release();
                }
                await TaskExtensions.RunOnMainThread(() => {
                    if (!token.IsCancellationRequested && version == Volatile.Read(ref _loadHeadersVersion)) {
                        LoadingFinishedEvent?.Invoke(true);
                    }
                });
                Plugin.Log.Info($"[ReplayManager] Loading took {stopwatch.Elapsed}");
            } finally {
                if (version == Volatile.Read(ref _loadHeadersVersion)) {
                    _loadHeadersTask = null;
                }
            }
        }

        private static void LoadReplayHeader(string path, CancellationToken token, long version) {
            if (LoadReplayInfo(path) is not { } replayInfo) {
                return;
            }

            var hash = replayInfo.CalculateReplayHash();
            if (hashedHeaders.ContainsKey(hash)) {
                Plugin.Log.Debug($"[ReplayManager] Replay info with the same hash already exists. Hash: {hash}");
                return;
            }

            var header = CreateReplayHeader(path, replayInfo);

            lock (headersLocker) {
                if (token.IsCancellationRequested || version != Volatile.Read(ref _loadHeadersVersion)) return;
                headers.Add(header);
                hashedHeaders.TryAdd(hash, header);
            }

            SyncNotifyReplaysAdded(token, version);
        }

        private static IReplayInfo? LoadReplayInfo(string path) {
            if (ReplayHeadersCache.TryGetInfoByPath(path, out var info)) {
                return info;
            }

            var replayInfo = FileManager.ReadReplayInfo(path);
            ReplayHeadersCache.AddInfoByPath(path, replayInfo);

            if (replayInfo != null) {
                SaturateReplayInfo(replayInfo, path);
                info = replayInfo;
            }

            return info;
        }

        internal static async Task<Replay?> LoadReplayAsync(IReplayHeader header, CancellationToken token) {
            var path = header.FilePath;
            await replayOperations.WaitAsync(token);
            try {
                var replay = await FileManager.ReadReplayAsync(path, token);
                if (replay != null) {
                    SaturateReplayInfo(replay.info, path);
                    ReplayHeadersCache.AddInfoByPath(path, replay.info);
                }
                return replay;
            } finally {
                replayOperations.Release();
            }
        }

        #endregion

        #region ReplayManager SaveReplay

        public static IReplayHeader? LastPlayedReplay { get; private set; }

        /// <summary>
        /// Writes a replay performing configuration checks.
        /// </summary>
        /// <param name="playEndData">Used for name formatting and validation checks, cannot be omitted.</param>
        public static async Task<ReplaySavingResult> SaveReplayAsync(Replay replay, PlayEndData playEndData, CancellationToken token) {
            LastPlayedReplay = null;

            if (!ShouldSaveReplay(replay, playEndData)) {
                Plugin.Log.Info("[ReplayManager] Validation failed, replay will not be saved!");
                LastPlayedReplay = ReplayManager.CreateTempReplayHeader(replay, null);
                return new(ReplaySavingError.ValidationFailed);
            }

            var overrideOldReplays = ConfigFileData.Instance.OverrideOldReplays;
            ReplaySavingResult result;
            await replayOperations.WaitAsync(token);
            try {
                if (overrideOldReplays) {
                    Plugin.Log.Warn("[ReplayManager] OverrideOldReplays is enabled, old replays will be deleted");
                    await DeleteSimilarReplaysAsync(replay, token);
                }
                SaturateReplay(replay, playEndData);
                result = await SaveAnyReplayInternalAsync(replay, playEndData, token);
            } finally {
                replayOperations.Release();
            }
            if (result.Header != null) ReplayAddedEvent?.Invoke(result.Header);
            return result;
        }

        /// <summary>
        /// Writes a replay without any validity or config checks.
        /// </summary>
        /// <param name="playEndData">Used for name formatting, not too important.</param>
        public static async Task<ReplaySavingResult> SaveAnyReplayAsync(Replay replay, PlayEndData? playEndData, CancellationToken token) {
            ReplaySavingResult result;
            await replayOperations.WaitAsync(token);
            try {
                result = await SaveAnyReplayInternalAsync(replay, playEndData, token);
            } finally {
                replayOperations.Release();
            }
            if (result.Header != null) ReplayAddedEvent?.Invoke(result.Header);
            return result;
        }

        private static async Task<ReplaySavingResult> SaveAnyReplayInternalAsync(Replay replay, PlayEndData? playEndData, CancellationToken token) {
            var hash = replay.info.CalculateReplayHash();

            if (hashedHeaders.TryGetValue(hash, out _)) {
                return new(ReplaySavingError.AlreadyExists);
            }

            var name = FormatFileName(replay, playEndData);
            Plugin.Log.Info($"[ReplayManager] Replay will be saved as: {name}");

            if (!await FileManager.WriteReplayAsync(name, replay, token)) {
                Plugin.Log.Error("[ReplayManager] Failed to write replay");

                return new(ReplaySavingError.WritingFailed);
            }

            var absolutePath = FileManager.GetAbsoluteReplayPath(name);
            var header = CreateReplayHeader(absolutePath, replay.info);

            LastPlayedReplay = header;
            lock (headersLocker) {
                headers.Add(header);
            }
            hashedHeaders.TryAdd(hash, header);

            ReplayHeadersCache.AddInfoByPath(header.FilePath, header.ReplayInfo);

            return new(header);
        }

        /// <summary>
        /// Creates a temporary replay header without adding it to the headers list.
        /// </summary>
        /// <param name="replay">A replay to provide when calling <see cref="IReplayHeader.LoadReplayAsync"/>.</param>
        /// <param name="player">A player to provide when calling <see cref="IReplayHeader.LoadPlayerAsync"/> or null to load it later.</param>
        /// <returns>A temporary header.</returns>
        public static IReplayHeader CreateTempReplayHeader(Replay replay, Player? player) {
            return new TempReplayHeader(replay, player);
        }

        #endregion

        #region Delete

        /// <summary>
        /// Deletes all replays.
        /// </summary>
        /// <returns>A count of successfully deleted items.</returns>
        internal static async Task<int> DeleteAllReplaysAsync() {
            await Task.Yield();
            if (IsLoading) CancelLoading(false);
            await replayOperations.WaitAsync();
            int deletedReplays;
            try {
                if (IsLoading) CancelLoading(false);
                else Interlocked.Increment(ref _loadHeadersVersion);
                var directories = FileManager.GetReplayDirectories();
                ReplayHeadersCache.ClearInfo();
                ReplayHeadersCache.SaveCache();
                ReplayMetadataManager.ClearMetadata();

                deletedReplays = await Task.Run(() => {
                    var count = 0;
                    var paths = Directory.EnumerateFiles(directories[0], ReplayFilePattern)
                        .Concat(Directory.EnumerateFiles(directories[1], ReplayFilePattern));
                    foreach (var path in paths) {
                        try {
                            File.Delete(path);
                        } catch (Exception ex) {
                            Plugin.Log.Error($"Failed to delete a replay:\n{ex}");
                            continue;
                        }
                        count++;
                    }
                    return count;
                });
                lock (headersLocker) {
                    headers.Clear();
                    hashedHeaders.Clear();
                    _lastBatchIndex = 0;
                }
            } finally {
                replayOperations.Release();
            }
            AllReplaysDeletedEvent?.Invoke();

            return deletedReplays;
        }

        /// <summary>
        /// Deletes a single replay.
        /// </summary>
        internal static async Task DeleteReplayAsync(IReplayHeader header) {
            var path = header.FilePath;
            await replayOperations.WaitAsync();
            try {
                ReplayHeadersCache.RemoveInfoByPath(path);
                ReplayHeadersCache.SaveCache();
                ReplayMetadataManager.DeleteMetadata(path);
                await Task.Run(() => File.Delete(path));
                lock (headersLocker) {
                    for (var i = 0; i < headers.Count; i++) {
                        var current = headers[i];
                        if (!ReferenceEquals(current, header) && current.FilePath != path) continue;
                        // A completed scan may have replaced the selected header during admission.
                        header = current;
                        headers.RemoveAt(i);
                        break;
                    }
                }
            } finally {
                replayOperations.Release();
            }
            NotifyReplayDeleted(header);
        }

        #endregion

        #region Delete Internal

        private static void FinalizeReplayDeletion(IReplayHeader header) {
            lock (headersLocker) {
                headers.Remove(header);
            }

            NotifyReplayDeleted(header);
        }

        private static void NotifyReplayDeleted(IReplayHeader header) {
            (header as PhysicalReplayHeader)?.NotifyReplayDeleted();
            ReplayDeletedEvent?.Invoke(header);
        }

        private static void DeleteReplayInternal(string filePath) {
            ReplayHeadersCache.RemoveInfoByPath(filePath);
            ReplayHeadersCache.SaveCache();

            ReplayMetadataManager.DeleteMetadata(filePath);
            File.Delete(filePath);
        }

        private static async Task DeleteSimilarReplaysAsync(Replay replay, CancellationToken token) {
            var info = replay.info;
            var buffer = new List<IReplayHeader>();
            IReplayHeader[] snapshot;
            lock (headersLocker) {
                snapshot = headers.ToArray();
            }

            await Task.Run(
                () => {
                    foreach (var header in snapshot) {
                        if (!CompareReplayInfoForRemoval(header.ReplayInfo, info)) {
                            continue;
                        }

                        Plugin.Log.Info("[ReplayManager] Deleting old replay: " + Path.GetFileName(header.FilePath));

                        DeleteReplayInternal(header.FilePath);
                        buffer.Add(header);
                    }
                },
                token
            );

            foreach (var header in buffer) {
                FinalizeReplayDeletion(header);
            }
        }

        #endregion

        #region Cache

        internal static void LoadCache() {
            ReplayMetadataManager.LoadCache();
            ReplayHeadersCache.LoadCache();
        }

        internal static void SaveCache() {
            ReplayMetadataManager.SaveCache();
            ReplayHeadersCache.SaveCache();
        }

        public static void ClearHeadersCache() {
            ReplayHeadersCache.ClearInfo();
        }

        #endregion

        #region Tools

        private static bool CompareReplayInfoForRemoval(IReplayInfo? left, IReplayInfo? right) {
            if (left == null || right == null) {
                return false;
            }

            return left.PlayerID == right.PlayerID
                && left.SongName == right.SongName
                && left.SongDifficulty == right.SongDifficulty
                && left.SongMode == right.SongMode
                && left.SongHash == right.SongHash;
        }

        private static PhysicalReplayHeader CreateReplayHeader(string path, IReplayInfo replayInfo) {
            var meta = ReplayMetadataManager.GetMetadata(path);
            return new PhysicalReplayHeader(path, replayInfo, meta);
        }

        // BSOR v1 does not encode the completion type; retain the gameplay result before saving.
        private static void SaturateReplay(Replay replay, PlayEndData data) {
            replay.info.levelEndType = data.EndType;
        }

        internal static void SaturateReplayInfo(ReplayInfo info, string? path) {
            if (info.hash.Length > 40 && !info.hash.EndsWith("WIP")) {
                info.hash = info.hash.Substring(0, 40);
            }

            if (info.mode is var mode && mode.IndexOf('-') is var idx and not -1) {
                info.mode = mode.Remove(idx, mode.Length - idx);
            }

            if (path != null && Path.GetFileName(path).Contains("exit")) {
                info.levelEndType = LevelEndType.Quit;
            }

            if (path != null && Path.GetFileName(path).Contains("practice")) {
                info.levelEndType = LevelEndType.Practice;
            }
        }

        private static bool ShouldSaveReplay(Replay replay, PlayEndData endData) {
            var options = ConfigFileData.Instance.ReplaySavingOptions;

            return ConfigFileData.Instance.SaveLocalReplays && endData.EndType switch {
                LevelEndType.Fail => options.HasFlag(ReplaySaveOption.Fail),
                LevelEndType.Practice => options.HasFlag(ReplaySaveOption.Practice),
                LevelEndType.Quit or LevelEndType.Restart => options.HasFlag(ReplaySaveOption.Exit),
                LevelEndType.Clear => true,
                _ => false
            } && (options.HasFlag(ReplaySaveOption.ZeroScore) || replay.info.score != 0);
        }

        private static string FormatFileName(Replay replay, PlayEndData? playEndData) {
            var practice = replay.info.speed != 0 ? "-practice" : "";
            var fail = replay.info.failTime != 0 ? "-fail" : "";

            var exit = playEndData?.EndType
                is LevelEndType.Quit
                or LevelEndType.Restart
                ? "-exit" : "";

            var info = replay.info;
            var filename = $"{info.playerID}{practice}{fail}{exit}-{info.songName}-{info.difficulty}-{info.mode}-{info.hash}-{info.timestamp}{ReplayFileExtension}";

            var regexSearch = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            var r = new Regex($"[{Regex.Escape(regexSearch)}]", RegexOptions.None, TimeSpan.FromSeconds(1));

            return r.Replace(filename, "_");
        }

        #endregion
    }
}
