using System;
using System.Collections;
using System.Reflection;
using BeatLeader.Attributes;
using BeatLeader.Utils;
using Zenject;

namespace BeatLeader.Interop {
    [PluginInterop("Chroma")]
    internal static class ChromaInterop {
        [PluginState]
        public static bool IsInstalled { get; private set; }

        [PluginType("Chroma.Colorizer.LightColorizerManager")]
        private static Type? _managerType = default!;

        private static object? _manager;
        private static IDictionary? _colorizers;
        private static MethodInfo? _resetMethod;

        private static MethodInfo? _finishMethod;

        public static bool IsReplaySeekTrackingActive => _manager != null && _resetMethod != null && _finishMethod != null;

        public static void BeginReplaySeekTracking(DiContainer container) {
            if (!IsInstalled || _managerType == null) return;

            try {
                _manager = container.TryResolve(_managerType);
                if (_manager == null) {
                    EndReplaySeekTracking();
                    return;
                }
                _colorizers = (IDictionary?)_managerType.GetProperty("Colorizers")?.GetValue(_manager);
                _resetMethod = _managerType.GetMethod("ResetForReplaySeek", ReflectionUtils.DefaultFlags);
                _finishMethod = _managerType.GetMethod("FinishReplaySeek", ReflectionUtils.DefaultFlags);
                if (_colorizers == null || _resetMethod == null || _finishMethod == null) {
                    Plugin.Log.Warn("Chroma replay lighting reset is unavailable");
                    EndReplaySeekTracking();
                } else {
                    Plugin.Log.Info($"Chroma replay lighting ready: {_colorizers.Count} event types");
                }
            } catch (Exception ex) {
                Plugin.Log.Error($"Failed to prepare Chroma replay lighting: {ex}");
                EndReplaySeekTracking();
            }
        }

        public static void EndReplaySeekTracking() {
            _manager = null;
            _colorizers = null;
            _resetMethod = null;
            _finishMethod = null;
        }

        public static bool IsLightingEvent(BasicBeatmapEventData item) =>
            IsReplaySeekTrackingActive && _colorizers!.Contains(item.basicBeatmapEventType);

        public static void ResetForReplaySeek() {
            if (!IsReplaySeekTrackingActive) return;
            _resetMethod!.Invoke(_manager, null);
            Plugin.Log.Info("Reset Chroma lighting for replay seek");
        }

        public static void FinishReplaySeek(float songTime) {
            if (!IsReplaySeekTrackingActive) return;
            _finishMethod!.Invoke(_manager, new object[] { songTime });
        }
    }
}
