using System;
using System.Linq;
using System.Reflection;
using BeatLeader.Attributes;

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

        [InteropEntry]
        private static void Init() {
            _startParametersConstructor = _startParametersType
                .GetConstructors()
                .First(x => x.GetParameters().Any(parameter => parameter.Name == "beatmapKey"));
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
