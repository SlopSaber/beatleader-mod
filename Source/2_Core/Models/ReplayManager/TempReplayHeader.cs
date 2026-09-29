using System;
using System.Threading;
using System.Threading.Tasks;

namespace BeatLeader.Models {
    internal class TempReplayHeader : ReplayHeaderBase, IReplayHeader {
        public TempReplayHeader(Replay.Replay replay, Player? player) : base(player) {
            _replay = replay;
        }

        public override IReplayInfo ReplayInfo => _replay.info;
        public ReplayMetadata ReplayMetadata { get; } = new(); 
        public FileStatus FileStatus => FileStatus.Loaded;
        public string FilePath => "Temporary";

        // Temporary replays stay loaded for their entire lifetime.
        public event Action<FileStatus>? StatusChangedEvent { add { } remove { } }

        private readonly Replay.Replay _replay;

        public Task<Replay.Replay?> LoadReplayAsync(CancellationToken token) {
            return Task.FromResult(_replay)!;
        }
    }
}