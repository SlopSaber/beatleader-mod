using System.Linq;
using IPA.Utilities;
using UnityEngine;

namespace BeatLeader.Replayer.Emulation {
    public class NoteControllerEmulator : NoteController {
        public override NoteData? noteData => _emulatedNoteData;
        public NoteCutInfo CutInfo { get; private set; }

        private NoteController? _prefab;
        private NoteData? _emulatedNoteData;

        public override void Awake() {
            _prefab = Resources.FindObjectsOfTypeAll<BeatmapObjectsInstaller>()
                .FirstOrDefault().GetField<GameNoteController, BeatmapObjectsInstaller>("_normalBasicNotePrefab");
            _noteMovement = _prefab.GetField<NoteMovement, NoteController>("_noteMovement");
            _noteTransform = transform;
        }
        public void Setup(NoteData noteData, NoteCutInfo cutInfo) {
            _emulatedNoteData = noteData;
            CutInfo = cutInfo;
        }

        #region Garbage

        public override void ManualUpdate() { }
        public override void OnDestroy() { }
        public override void HiddenStateDidChange(bool _) { }
        public override void Pause(bool _) { }

        #endregion
    }
}