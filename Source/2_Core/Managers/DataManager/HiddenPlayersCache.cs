using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Models;
using BeatLeader.Utils;
using IPA.Utilities;
using Newtonsoft.Json;

namespace BeatLeader.DataManager {
    internal static class HiddenPlayersCache {
        #region Cache file

        private static readonly string CacheFileName = Path.Combine(UnityGame.UserDataPath, "BeatLeader", "HiddenPlayers");
        private static readonly object CacheGate = new();
        private static readonly Task<string?> _cacheReadTask;
        private static Task _pendingWriteTask = Task.CompletedTask;
        private static bool _initialized;
        private static bool _initializing;

        private static JsonSerializerSettings SerializerSettings => new() {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore
        };

        static HiddenPlayersCache() {
            _cacheReadTask = Task.Run<string?>(() => File.Exists(CacheFileName) ? File.ReadAllText(CacheFileName) : null);
        }

        public static void Prewarm() {
            _ = _cacheReadTask;
        }

        private static void LoadHiddenPlayersIfNeeded() {
            // JSON callbacks can reenter on the initializing thread while the gate is held.
            if (_initialized || _initializing) return;
            _initializing = true;
            try {
                var text = _cacheReadTask.GetAwaiter().GetResult();
                if (text != null) {
                    HiddenPlayers = JsonConvert.DeserializeObject<HashSet<string>>(text, SerializerSettings) ?? new HashSet<string>();
                }
            } catch (Exception e) {
                Plugin.Log.Debug($"HiddenPlayers cache load failed! {e}");
            } finally {
                _initializing = false;
                _initialized = true;
            }
        }

        private static void SaveHiddenPlayersCache() {
            try {
                lock (CacheGate) {
                    var text = JsonConvert.SerializeObject(HiddenPlayers, SerializerSettings);
                    _pendingWriteTask = _pendingWriteTask.ContinueWith(static (previous, state) => {
                        _ = previous.Exception;
                        WriteHiddenPlayersCache((string)state!);
                    }, text, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                }
            } catch (Exception e) {
                Plugin.Log.Debug($"HiddenPlayers cache save failed! {e}");
            }
        }

        private static void WriteHiddenPlayersCache(string text) {
            try {
                FileManager.EnsureDirectoryExists(CacheFileName);
                File.WriteAllText(CacheFileName, text);
            } catch (Exception e) {
                Plugin.Log.Debug($"HiddenPlayers cache save failed! {e}");
            }
        }

        public static void FlushPendingWrites() {
            Task pending;
            lock (CacheGate) pending = _pendingWriteTask;
            try {
                pending.GetAwaiter().GetResult();
            } catch (Exception e) {
                Plugin.Log.Debug($"HiddenPlayers cache save failed! {e}");
            }
        }

        #endregion

        #region Logic

        public static event Action HiddenPlayersUpdatedEvent {
            add {
                lock (CacheGate) {
                    LoadHiddenPlayersIfNeeded();
                    _hiddenPlayersUpdatedEvent += value;
                }
            }
            remove {
                lock (CacheGate) {
                    LoadHiddenPlayersIfNeeded();
                    _hiddenPlayersUpdatedEvent -= value;
                }
            }
        }

        private static Action? _hiddenPlayersUpdatedEvent;
        private static HashSet<string> HiddenPlayers = new();

        public static void HidePlayer(Player player) {
            lock (CacheGate) {
                LoadHiddenPlayersIfNeeded();
                HiddenPlayers.Add(player.id);
            }
            _hiddenPlayersUpdatedEvent?.Invoke();
            SaveHiddenPlayersCache();
        }

        public static void RevealPlayer(Player player) {
            lock (CacheGate) {
                LoadHiddenPlayersIfNeeded();
                HiddenPlayers.Remove(player.id);
            }
            _hiddenPlayersUpdatedEvent?.Invoke();
            SaveHiddenPlayersCache();
        }

        public static Player HidePlayerIfNeeded(Player player) {
            if (!IsHidden(player)) return player;
            
            return new Player() {
                id = player.id,
                rank = 0,
                name = "~hidden player~",
                country = "not set",
                countryRank = 0,
                experience = 0,
                level = 0,
                prestige = 0,
                pp = 0f,
                role = "",
                clans = Array.Empty<Clan>(),
                socials = Array.Empty<ServiceIntegration>(),
                profileSettings = null
            };
        }

        public static bool IsHidden(Player? player) {
            lock (CacheGate) {
                LoadHiddenPlayersIfNeeded();
                return player != null && HiddenPlayers.Contains(player.id);
            }
        }

        #endregion
    }
}
