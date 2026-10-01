using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Utils;

namespace BeatLeader.SteamVR {
    internal static class SteamVRSettings {
        #region Settings

        private static readonly ConcurrentDictionary<string, SettingValue> settings = new();

        private static readonly NumberFormatInfo nf = new NumberFormatInfo() {
            NumberDecimalSeparator = "."
        };

        private readonly struct SettingValue {
            public SettingValue(string text) {
                Text = text;
                FloatValue = float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, nf, out var value)
                    ? value
                    : (float?)null;
            }

            public readonly string Text;
            public readonly float? FloatValue;
        }

        public static bool IsAvailable() {
            return !settings.IsEmpty;
        }

        public static float GetFloatOrDefault(string key, float defaultValue = default) {
            return settings.TryGetValue(key, out var setting) ? setting.FloatValue ?? defaultValue : defaultValue;
        }

        public static string? GetString(string key) {
            return settings.TryGetValue(key, out var setting) ? setting.Text : null;
        }

        #endregion

        #region Update

        private const int TimeoutSeconds = 15;

        public static void UpdateAsync() {
            _ = Task.Run(async () => {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
                await UpdateTask(cts.Token);
            }).RunCatching();
        }

        private static async Task UpdateTask(CancellationToken cancellationToken) {
            //<- Connect --------------------------------------------------
            using var session = new SteamVRWebConsoleSession();
            var (state, failReason) = await session.ConnectTask(cancellationToken);

            if (state is not SteamVRWebConsoleSession.State.Opened) {
                Plugin.Log.Debug($"SteamVR console connection failed: {failReason}");
                return;
            }

            //<- Send 'settings' command ----------------------------------
            if (!session.TrySendCommandRequest("settings", out failReason)) {
                Plugin.Log.Debug($"SteamVR console settings request failed: {failReason}");
                return;
            }

            //<- Process response messages until timeout ------------------
            var parseState = ParseState.SearchForSettingsBlock;
            await session.ListenTask(message => ProcessMessage(message, ref parseState), cancellationToken);
            Plugin.Log.Debug($"SteamVR settings received: {settings.Count}");
        }

        #endregion

        #region ProcessMessages

        private enum ParseState {
            SearchForSettingsBlock,
            SkipFirstDashes,
            ParseSettings
        }

        private static void ProcessMessage(SteamVRWebConsoleSession.Message message, ref ParseState state) {
            var line = message.sMessage;

            var contentStart = line.IndexOf("[Console]", StringComparison.Ordinal);
            if (contentStart < 0) return;
            var content = line.Substring(contentStart + 10).TrimEnd();

            switch (state) {
                case ParseState.SearchForSettingsBlock:
                    if (content.StartsWith("Settings:")) state = ParseState.SkipFirstDashes;
                    break;

                case ParseState.SkipFirstDashes:
                    if (content.StartsWith("--")) state = ParseState.ParseSettings;
                    break;

                case ParseState.ParseSettings:
                    if (content.StartsWith("--")) {
                        state = ParseState.SearchForSettingsBlock;
                        break;
                    }

                    var separatorIndex = content.IndexOf(": ", StringComparison.Ordinal);
                    if (separatorIndex < 0) return;

                    var key = content.Substring(0, separatorIndex);
                    var value = content.Substring(separatorIndex + 2);
                    settings[key] = new SettingValue(value);
                    break;
            }
        }

        #endregion
    }
}
