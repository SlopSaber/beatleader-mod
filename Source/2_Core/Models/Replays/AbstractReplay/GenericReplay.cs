using System.Collections.Generic;

using System.Threading;

namespace BeatLeader.Models.AbstractReplay {
    public class GenericReplay : IReplay {
        public GenericReplay(
            IReplayData replayData,
            IReplayNoteComparator noteComparator,
            BattleRoyaleReplayData? optionalReplayData,
            IReadOnlyList<PlayerMovementFrame> movementFrames,
            IReadOnlyList<NoteEvent> noteEvents,
            IReadOnlyList<WallEvent> wallEvents,
            IReadOnlyList<PauseEvent> pauseEvents,
            IReadOnlyList<HeightEvent>? heightEvents,
            IReadOnlyDictionary<string, byte[]> customData
        ) {
            ReplayData = replayData;
            NoteComparator = noteComparator;
            OptionalReplayData = optionalReplayData;
            _movementFrames = movementFrames;
            NoteEvents = noteEvents;
            WallEvents = wallEvents;
            PauseEvents = pauseEvents;
            HeightEvents = heightEvents;
            CustomData = customData;
        }

        public IReplayData ReplayData { get; }
        public IReplayNoteComparator NoteComparator { get; }
        public BattleRoyaleReplayData? OptionalReplayData { get; }
        private readonly IReadOnlyList<PlayerMovementFrame> _movementFrames;
        private LinkedList<PlayerMovementFrame>? _preparedMovementFrames;

        public IReadOnlyList<PlayerMovementFrame> PlayerMovementFrames {
            get {
                // Public access exposes the backing collection, which may change before startup.
                Interlocked.Exchange(ref _preparedMovementFrames, null);
                return _movementFrames;
            }
        }

        internal void PrepareMovementFrames(CancellationToken token) {
            var frames = new LinkedList<PlayerMovementFrame>();
            foreach (var frame in _movementFrames) {
                token.ThrowIfCancellationRequested();
                frames.AddLast(frame);
            }
            _preparedMovementFrames = frames;
        }

        internal LinkedList<PlayerMovementFrame>? TakePreparedMovementFrames() {
            return Interlocked.Exchange(ref _preparedMovementFrames, null);
        }

        public IReadOnlyList<NoteEvent> NoteEvents { get; }
        public IReadOnlyList<WallEvent> WallEvents { get; }
        public IReadOnlyList<PauseEvent> PauseEvents { get; }

        public IReadOnlyList<HeightEvent>? HeightEvents { get; }
        public IReadOnlyDictionary<string, byte[]> CustomData { get; }
    }
}
