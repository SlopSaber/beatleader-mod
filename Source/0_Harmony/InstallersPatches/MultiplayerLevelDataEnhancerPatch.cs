using BeatLeader.Core.Managers.ReplayEnhancer;
using HarmonyLib;

namespace BeatLeader {
    [HarmonyPatch(typeof(MultiplayerLevelScenesTransitionSetupDataSO), nameof(MultiplayerLevelScenesTransitionSetupDataSO.Init))]
    class MultiplayerLevelDataEnhancerPatch
    {
        static void Postfix(MultiplayerLevelScenesTransitionSetupDataSO __instance, in BeatmapKey beatmapKey, BeatmapLevel beatmapLevel, IBeatmapLevelData beatmapLevelData, ColorScheme overrideColorScheme, GameplayModifiers gameplayModifiers, PlayerSpecificSettings playerSpecificSettings, AudioClipAsyncLoader audioClipAsyncLoader, BeatmapDataLoader beatmapDataLoader)
        {
            Plugin.Log.Debug("MultiplayerLevelDataEnhancerPatch.postfix");
            MapEnhancer.beatmapKey = beatmapKey;
            MapEnhancer.beatmapLevel = beatmapLevel;
            MapEnhancer.gameplayModifiers = gameplayModifiers;
            MapEnhancer.playerSpecificSettings = playerSpecificSettings;
#if BEAT_SABER_1453
            MapEnhancer.environmentName = beatmapLevel.GetEnvironmentName(in beatmapKey);
#else
            MapEnhancer.environmentName = beatmapLevel.GetEnvironmentName(beatmapKey.characteristic, beatmapKey.difficulty);
#endif
            MapEnhancer.colorScheme = overrideColorScheme;
        }
    }
}
