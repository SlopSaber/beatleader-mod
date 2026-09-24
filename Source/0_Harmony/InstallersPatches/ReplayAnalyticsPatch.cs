using BeatLeader.Replayer;
using HarmonyLib;

namespace BeatLeader {
    [HarmonyPatch(typeof(StandardLevelAnalytics), "HandleStandardLevelDidFinishEvent")]
    internal static class ReplayAnalyticsPatch {
        private static bool Prefix() => !ReplayerLauncher.IsStartedAsReplay;
    }
}
