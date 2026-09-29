using BeatLeader.Models;
using BeatLeader.Models.AbstractReplay;
using System.Linq;
using Zenject;

namespace BeatLeader.Replayer.Tweaking {
    internal class ReplayFailTweak : GameTweak {
        [Inject] private readonly IReplayTimeController _timeController = null!;
        [Inject] private readonly GameEnergyCounter _energyCounter = null!;
        [Inject] private readonly ReplayLaunchData _launchData = null!;

        public override void Initialize() {
            _timeController.SongReachedReplayEndEvent += HandleReplayFinished;
        }

        public override void Dispose() {
            _timeController.SongReachedReplayEndEvent -= HandleReplayFinished;
        }

        private void HandleReplayFinished() {
            // The shared timeline ends after the longest replay. A battle fails only
            // when every replay failed; a cleared or incomplete replay is not a failure.
            if (!_launchData.Replays.All(replay => replay.ReplayData.FinishType == ReplayFinishType.Failed)) return;
            _energyCounter.ProcessEnergyChange(-1f);
        }
    }
}