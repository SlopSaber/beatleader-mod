using BeatLeader.Interop;
using BeatLeader.Models;
using Zenject;

namespace BeatLeader.Replayer.Tweaking {
    internal class InteropsLoaderTweak : GameTweak {
        [Inject] private readonly IVirtualPlayersManager _playersManager = null!;
        [Inject] private readonly IBeatmapTimeController _beatmapTimeController = null!;
        [Inject] private readonly BeatmapObjectManager _beatmapObjectManager = null!;
        [Inject] private readonly ReplayerExtraObjectsProvider _extraObjects = null!;
        [Inject] private readonly DiContainer _container = null!;

        public override void Initialize() {
            HeckInterop.BeginReplaySeekTracking(_container);
            HandlePrimaryPlayerChanged(_playersManager.PrimaryPlayer);
            _playersManager.PrimaryPlayerWasChangedEvent += HandlePrimaryPlayerChanged;
            _beatmapObjectManager.noteWasDespawnedEvent += HandleNoteWasDespawned;
            _beatmapTimeController.EarlySongWasRewoundEvent += HandleSongWasRewound;
        }

        public override void Dispose() {
            HeckInterop.EndReplaySeekTracking();
            Cam2Interop.UnbindMovementProcessor();
            _playersManager.PrimaryPlayerWasChangedEvent -= HandlePrimaryPlayerChanged;
            _beatmapObjectManager.noteWasDespawnedEvent -= HandleNoteWasDespawned;
            _beatmapTimeController.EarlySongWasRewoundEvent -= HandleSongWasRewound;
        }

        private void HandlePrimaryPlayerChanged(IVirtualPlayer player) {
            Cam2Interop.UnbindMovementProcessor();
            Cam2Interop.BindMovementProcessor(player.MovementProcessor, _extraObjects);
        }

        private void HandleNoteWasDespawned(NoteController controller) {
            CustomNotesInterop.TryDespawnCustomObject(controller);
        }

        private void HandleSongWasRewound(float time) {
            NoodleExtensionsInterop.RequestReprocess();
        }
    }
}
