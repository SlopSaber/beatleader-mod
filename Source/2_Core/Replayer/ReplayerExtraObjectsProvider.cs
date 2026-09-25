using BeatLeader.Utils;
using UnityEngine;
using Zenject;

namespace BeatLeader.Replayer {
    internal class ReplayerExtraObjectsProvider : MonoBehaviour {
        [Inject] private readonly PlayerTransforms _playerTransforms = null!;
        [FirstResource]
        private readonly MainSystemInit _mainSystemInit = null!;

        [FirstResource(Name = "VRGameCore", RequireActiveInHierarchy = true)]
        private readonly Transform _origin = null!;

        public Transform ReplayerCore => transform;
        public Transform ReplayerCenterAdjust { get; private set; } = null!;
        public Transform VRGameCore => _origin;
        public Transform ReplayPoseOrigin => _playerTransforms._originParentTransform != null
            ? _playerTransforms._originParentTransform
            : VRGameCore;

        private Vector3 _posOffset;
        private Quaternion _rotOffset;

        private void Awake() {
            this.LoadResources();
            // Replay poses were recorded relative to this transform. Noodle moves it
            // when a map assigns the player to a track.
            ReplayerCore.SetParent(ReplayPoseOrigin, false);
            name = "ReplayerCore";

            ReplayerCenterAdjust = new GameObject("CenterAdjust").transform;
            ReplayerCenterAdjust.SetParent(ReplayerCore, false);

            var settingsModel = _mainSystemInit._settingsManager.settings;
            _posOffset = settingsModel.room.center;
            _rotOffset = Quaternion.Euler(0, settingsModel.room.rotation, 0);
        }

        private void Start() {
            ApplyOffsets();
        }

        private void OnEnable() {
            ApplyOffsets();
        }

        private void ApplyOffsets() {
            if (InputUtils.UsesFPFC) return;
            ReplayerCenterAdjust.localPosition = _posOffset;
            ReplayerCenterAdjust.localRotation = _rotOffset;
        }
    }
}
