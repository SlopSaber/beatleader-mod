using BeatLeader.Core.Managers.ReplayEnhancer;
using HarmonyLib;
using System.Linq;
using System.Reflection;

namespace BeatLeader
{
    [HarmonyPatch(typeof(StandardLevelScenesTransitionSetupDataSO), "Init")]

    class LevelDataEnhancerPatch {
        public static void Postfix(
        ref BeatmapKey beatmapKey,
        BeatmapLevel beatmapLevel,
        OverrideEnvironmentSettings? overrideEnvironmentSettings,
        ColorScheme? playerOverrideColorScheme,
        bool playerOverrideLightshowColors,
        GameplayModifiers gameplayModifiers,
        PlayerSpecificSettings playerSpecificSettings,
        PracticeSettings? practiceSettings,
        EnvironmentsListModel environmentsListModel) {
            Plugin.Log.Debug($"LevelDataEnhancerPatch.postfix {beatmapKey.levelId} {beatmapLevel.songName}");

#if BEAT_SABER_1453
            string environmentName = beatmapLevel.GetEnvironmentName(in beatmapKey);
#else
            string environmentName = beatmapLevel.GetEnvironmentName(beatmapKey.characteristic, beatmapKey.difficulty);
#endif

            if (overrideEnvironmentSettings?.overrideEnvironments == true) {
                environmentName = overrideEnvironmentSettings
                    .GetOverrideEnvironmentInfoForType(
                        environmentsListModel
                            .GetEnvironmentInfoBySerializedName(environmentName)
                            .environmentType)
                    .environmentName;
            }

            MapEnhancer.beatmapKey = beatmapKey;
            MapEnhancer.beatmapLevel = beatmapLevel;
            MapEnhancer.gameplayModifiers = gameplayModifiers;
            MapEnhancer.playerSpecificSettings = playerSpecificSettings;
            MapEnhancer.practiceSettings = practiceSettings;
            MapEnhancer.environmentName = environmentName;
            MapEnhancer.colorScheme = playerOverrideColorScheme;
        }
    }
}
