using System;
using System.Linq;
using System.Reflection;
using BeatLeader.Attributes;
using BeatLeader.Utils;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace BeatLeader.Interop {
    [PluginInterop("Heck")]
    internal static class HeckInterop {
        [PluginState]
        public static bool IsInstalled { get; private set; }

        [PluginAssembly]
        public static Assembly? Assembly { get; private set; }

        [PluginType("Heck.PlayView.IPlayViewController")]
        public static Type? PlayViewControllerType { get; private set; }
        
        [PluginType("Heck.PlayView.PlayViewManager")]
        public static Type? PlayViewManagerType { get; private set; }
        
        [PluginType("Heck.PlayView.PlayViewManager+PlayViewControllerData")]
        public static Type? PlayViewControllerDataType { get; private set; }
        
        [PluginType("Heck.PlayView.StartStandardLevelParameters")]
        private static Type _startParametersType = null!;

        private static ConstructorInfo _startParametersConstructor = null!;
        private static readonly Dictionary<Component, TransformState> _initialTransforms = new();
        private static HarmonyAutoPatch? _transformEnablePatch;
        private static IDictionary? _tracks;
        private static MonoBehaviour? _coroutineDummy;
        private static MethodInfo? _nullPropertiesMethod;
        private static List<BeatmapDataItem>? _animationEvents;

        private sealed class TransformState {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;

            public TransformState(Transform transform) {
                Position = transform.localPosition;
                Rotation = transform.localRotation;
                Scale = transform.localScale;
            }
        }

        [InteropEntry]
        private static void Init() {
            _startParametersConstructor = _startParametersType
                .GetConstructors()
                .First(x => x.GetParameters().Any(parameter => parameter.Name == "beatmapKey"));
        }

        public static bool IsReplaySeekTrackingActive => _transformEnablePatch != null;

        public static void BeginReplaySeekTracking(DiContainer container) {
            if (!IsInstalled || Assembly == null || _transformEnablePatch != null) return;

            try {
                var trackType = Assembly.GetType("Heck.Animation.Track", true)!;
                var dummyType = Assembly.GetType("Heck.CoroutineDummy", true)!;
                var transformType = Assembly.GetType("Heck.Animation.Transform.TransformController", true)!;
                var tracksType = typeof(Dictionary<,>).MakeGenericType(typeof(string), trackType);
                _tracks = container.TryResolve(tracksType) as IDictionary;
                _coroutineDummy = container.TryResolve(dummyType) as MonoBehaviour;
                if (_tracks == null || _coroutineDummy == null) {
                    EndReplaySeekTracking();
                    return;
                }
                _nullPropertiesMethod = trackType.GetMethod("NullProperties", ReflectionUtils.DefaultFlags)!;
                var onEnable = transformType.GetMethod("OnEnable", ReflectionUtils.DefaultFlags)!;
                var capture = typeof(HeckInterop).GetMethod(nameof(CaptureInitialTransform), ReflectionUtils.StaticFlags)!;
                _transformEnablePatch = new HarmonyPatchDescriptor(onEnable, capture);
            } catch (Exception ex) {
                Plugin.Log.Error($"Failed to prepare Heck replay seeking: {ex}");
                EndReplaySeekTracking();
            }
        }

        public static void EndReplaySeekTracking() {
            _transformEnablePatch?.Dispose();
            _transformEnablePatch = null;
            _initialTransforms.Clear();
            _animationEvents = null;
            _tracks = null;
            _coroutineDummy = null;
            _nullPropertiesMethod = null;
        }

        public static void RebuildAnimations(IReadonlyBeatmapData beatmapData, CallbacksInTime callbacks, float songTime, bool includeCurrentTime) {
            if (!IsReplaySeekTrackingActive) return;

            _coroutineDummy!.StopAllCoroutines();
            ChromaInterop.ResetForReplaySeek();
            foreach (var track in _tracks!.Values) {
                _nullPropertiesMethod!.Invoke(track, null);
            }

            foreach (var pair in _initialTransforms) {
                if (pair.Key == null) continue;
                var transform = pair.Key.transform;
                transform.localPosition = pair.Value.Position;
                transform.localRotation = pair.Value.Rotation;
                transform.localScale = pair.Value.Scale;
            }

            _animationEvents ??= FindAnimationEvents(beatmapData);
            foreach (var item in _animationEvents) {
                if (includeCurrentTime ? item.time > songTime : item.time >= songTime) break;
                callbacks.CallCallbacks(item);
            }
            ChromaInterop.FinishReplaySeek(songTime);
        }

        private static List<BeatmapDataItem> FindAnimationEvents(IReadonlyBeatmapData beatmapData) {
            var events = new List<BeatmapDataItem>();
            PropertyInfo? eventTypeProperty = null;
            foreach (var item in beatmapData.allBeatmapDataItems) {
                if (item is BasicBeatmapEventData lightEvent) {
                    if (ChromaInterop.IsLightingEvent(lightEvent)) events.Add(item);
                    continue;
                }
                if (item is ColorBoostBeatmapEventData && ChromaInterop.IsReplaySeekTrackingActive) {
                    events.Add(item);
                    continue;
                }
                if (item.GetType().FullName != "CustomJSONData.CustomBeatmap.CustomEventData") continue;
                eventTypeProperty ??= item.GetType().GetProperty("eventType");
                var eventType = (string?)eventTypeProperty?.GetValue(item);
                if (eventType is "AnimateTrack" or "AssignPathAnimation" or "AnimateComponent") {
                    events.Add(item);
                }
            }
            return events;
        }

        private static void CaptureInitialTransform(object __instance) {
            var component = (Component)__instance;
            if (!_initialTransforms.ContainsKey(component)) {
                _initialTransforms.Add(component, new TransformState(component.transform));
            }
        }

        public static object CreateStartData(
            string gameMode,
            in BeatmapKey beatmapKey,
            BeatmapLevel beatmapLevel,
            OverrideEnvironmentSettings? overrideEnvironmentSettings,
            GameplayModifiers gameplayModifiers,
            PlayerSpecificSettings playerSpecificSettings
        ) {
            if (!IsInstalled) {
                throw new InvalidOperationException("Heck is not installed");
            }
            // Heck follows the game's launch signature. In 1.45.1 the trailing
            // recordingToolData parameter was removed; older builds still have it.
            // Bind known parameters by name so optional plugin versions can coexist.
            var parameters = _startParametersConstructor.GetParameters();
            var arguments = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++) {
                var parameter = parameters[i];
                arguments[i] = parameter.Name switch {
                    "gameMode" => gameMode,
                    "beatmapKey" => beatmapKey,
                    "beatmapLevel" => beatmapLevel,
                    "overrideEnvironmentSettings" => overrideEnvironmentSettings,
                    "playerOverrideLightshowColors" => false,
                    "gameplayModifiers" => gameplayModifiers,
                    "playerSpecificSettings" => playerSpecificSettings,
                    "overrideColorScheme" or "practiceSettings" or "environmentsListModel"
                        or "gameplayAdditionalInformation" or "beforeSceneSwitchToGameplayCallback"
                        or "afterSceneSwitchToGameplayCallback" or "levelFinishedCallback"
                        or "levelRestartedCallback" or "beatmapLevelData" or "recordingToolData" => null,
                    _ when parameter.HasDefaultValue => parameter.DefaultValue,
                    _ => throw new NotSupportedException($"Unsupported Heck replay launch parameter: {parameter.Name}")
                };
            }
            return _startParametersConstructor.Invoke(arguments);
        }
    }
}
