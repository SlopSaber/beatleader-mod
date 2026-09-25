using System;

namespace BeatLeader.Models {
    public interface IReplayTimeController : IBeatmapTimeController {
        float ReplayEndTime { get; }

        event Action SongReachedReplayEndEvent;
    }

    internal interface IReplayScrubController {
        void BeginScrub();
        void EndScrub();
    }
}
