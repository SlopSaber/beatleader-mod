using BeatLeader.Attributes;
using BeatLeader.Replayer;
using BeatLeader.Utils;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BeatLeader.Interop {
    [PluginInterop("BeatSaviorUI")]
    internal static class BeatSaviorUIInterop {
        [PluginType("BeatSaviorUI.Stats.LevelDataController")]
        private static readonly Type _levelDataControllerType = null!;

        [PluginType("BeatSaviorUI.Models.BeatmapInfo")]
        private static readonly Type _beatmapInfoType = null!;

        private static readonly ConditionalWeakTable<object, object> _replayLevels = new();
        private static PropertyInfo _songDataType = null!;
        private static object _replayType = null!;
        private static HarmonyAutoPatch? _initializePatch;
        private static HarmonyAutoPatch? _beatmapInfoPatch;

        [InteropEntry]
        private static void Init() {
            var initialize = _levelDataControllerType.GetMethod("Initialize", ReflectionUtils.DefaultFlags,
                null, Type.EmptyTypes, null) ?? throw new MissingMethodException(_levelDataControllerType.FullName, "Initialize");
            var getBeatmapInfo = _levelDataControllerType.GetMethod("GetBeatmapInfo", ReflectionUtils.DefaultFlags,
                null, Type.EmptyTypes, null) ?? throw new MissingMethodException(_levelDataControllerType.FullName, "GetBeatmapInfo");
            _songDataType = _beatmapInfoType.GetProperty("SongDataType", ReflectionUtils.DefaultFlags)
                ?? throw new MissingMemberException(_beatmapInfoType.FullName, "SongDataType");
            if (!_songDataType.CanWrite || !_songDataType.PropertyType.IsEnum || getBeatmapInfo.ReturnType != _beatmapInfoType) {
                throw new NotSupportedException("BeatSaviorUI does not expose writable replay metadata.");
            }
            _replayType = Enum.Parse(_songDataType.PropertyType, "Replay");

            var interop = typeof(BeatSaviorUIInterop);
            _initializePatch = new(new(initialize, prefix: interop.GetMethod(nameof(CaptureReplay), ReflectionUtils.StaticFlags)));
            try {
                _beatmapInfoPatch = new(new(getBeatmapInfo, postfix: interop.GetMethod(nameof(LabelReplay), ReflectionUtils.StaticFlags)));
            } catch {
                _initializePatch.Dispose();
                _initializePatch = null;
                throw;
            }
            Plugin.Log.Notice("BeatSaviorUI replay statistics integration loaded.");
        }

        private static void CaptureReplay(object __instance) {
            _replayLevels.Remove(__instance);
            // Completion handlers can clear the launch flag before statistics are finalized.
            if (ReplayerLauncher.IsStartedAsReplay) _replayLevels.Add(__instance, new object());
        }

        private static void LabelReplay(object __instance, object __result) {
            if (__result != null && _replayLevels.TryGetValue(__instance, out _)) {
                _songDataType.SetValue(__result, _replayType);
            }
        }
    }
}
