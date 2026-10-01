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
            _noteEvents = noteEvents;
            _wallEvents = wallEvents;
            PauseEvents = pauseEvents;
            _heightEvents = heightEvents;
            CustomData = customData;
        }

        public IReplayData ReplayData { get; }
        public IReplayNoteComparator NoteComparator { get; }
        public BattleRoyaleReplayData? OptionalReplayData { get; }
        private readonly IReadOnlyList<PlayerMovementFrame> _movementFrames;
        private readonly IReadOnlyList<NoteEvent> _noteEvents;
        private readonly IReadOnlyList<WallEvent> _wallEvents;
        private readonly IReadOnlyList<HeightEvent>? _heightEvents;
        private LinkedList<PlayerMovementFrame>? _preparedMovementFrames;
        private LinkedList<NoteEvent>? _preparedNoteEvents;
        private LinkedList<WallEvent>? _preparedWallEvents;
        private LinkedList<HeightEvent>? _preparedHeightEvents;

        public IReadOnlyList<PlayerMovementFrame> PlayerMovementFrames {
            get {
                // Public access exposes the backing collection, which may change before startup.
                Interlocked.Exchange(ref _preparedMovementFrames, null);
                return _movementFrames;
            }
        }

        // Prepare only before publication, from privately projected collections.
        internal void PreparePlaybackQueues(CancellationToken token) {
            _preparedMovementFrames = PrepareQueue(_movementFrames, token);
            _preparedNoteEvents = PrepareQueue(_noteEvents, token);
            _preparedWallEvents = PrepareQueue(_wallEvents, token);
            _preparedHeightEvents = _heightEvents is null ? null : PrepareQueue(_heightEvents, token);
        }

        private static LinkedList<T> PrepareQueue<T>(IReadOnlyList<T> events, CancellationToken token) where T : struct {
            var queue = new LinkedList<T>();
            foreach (var item in events) {
                token.ThrowIfCancellationRequested();
                queue.AddLast(item);
            }
            return queue;
        }

        internal LinkedList<PlayerMovementFrame>? TakePreparedMovementFrames() {
            return Interlocked.Exchange(ref _preparedMovementFrames, null);
        }

        internal LinkedList<NoteEvent>? TakePreparedNoteEvents() {
            return Interlocked.Exchange(ref _preparedNoteEvents, null);
        }

        internal LinkedList<WallEvent>? TakePreparedWallEvents() {
            return Interlocked.Exchange(ref _preparedWallEvents, null);
        }

        internal LinkedList<HeightEvent>? TakePreparedHeightEvents() {
            return Interlocked.Exchange(ref _preparedHeightEvents, null);
        }

        // Local scoring must not expose this list to callbacks.
        internal IReadOnlyList<NoteEvent> NoteEventsForScoring => _noteEvents;

        public IReadOnlyList<NoteEvent> NoteEvents {
            get {
                Interlocked.Exchange(ref _preparedNoteEvents, null);
                return _noteEvents;
            }
        }

        public IReadOnlyList<WallEvent> WallEvents {
            get {
                Interlocked.Exchange(ref _preparedWallEvents, null);
                return _wallEvents;
            }
        }
        public IReadOnlyList<PauseEvent> PauseEvents { get; }

        public IReadOnlyList<HeightEvent>? HeightEvents {
            get {
                Interlocked.Exchange(ref _preparedHeightEvents, null);
                return _heightEvents;
            }
        }
        public IReadOnlyDictionary<string, byte[]> CustomData { get; }
    }
}
