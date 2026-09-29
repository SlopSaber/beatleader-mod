using System;
using System.IO;
using IPA.Utilities;

namespace BeatLeader.Replayer {
    [Obsolete("Use ReplayManager instead")]
    internal static class ReplayerCache {
        public static readonly string CacheDirectory = Utils.ReplayManager.LegacyCacheDirectory;
    }
}