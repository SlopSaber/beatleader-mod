using BeatLeader.Utils;
using UnityEngine;
using Zenject;
using System;
using BeatLeader.Models;
using BeatLeader.Models.AbstractReplay;

namespace BeatLeader.Replayer {
    internal class ReplayerExtraObjectsProvider : MonoBehaviour {
        [Inject] private readonly PlayerTransforms _playerTransforms = null!;
        [Inject] private readonly ReplayLaunchData _launchData = null!;
        [FirstResource]
        private readonly MainSystemInit _mainSystemInit = null!;

        [FirstResource(Name = "VRGameCore", RequireActiveInHierarchy = true)]
        private readonly Transform _origin = null!;

        public Transform ReplayerCore => transform;
        public Transform ReplayerCenterAdjust { get; private set; } = null!;
        public Transform VRGameCore => _origin;
        public Transform ReplayPoseOrigin {
            get {
                if (UsesLegacyPseudoLocalPoses) {
                    var trackRoot = _playerTransforms._originTransform.parent?.parent;
                    if (trackRoot != null && trackRoot.name == "NoodlePlayerTransformRoot")
                        return trackRoot;
                }
                return _playerTransforms._originParentTransform != null
                    ? _playerTransforms._originParentTransform
                    : VRGameCore;
            }
        }

        private bool? _usesLegacyPseudoLocalPoses;
        private bool UsesLegacyPseudoLocalPoses {
            get {
                if (_usesLegacyPseudoLocalPoses.HasValue)
                    return _usesLegacyPseudoLocalPoses.Value;

                // Replays before v0.9.5 stored pseudo-local poses without Noodle player motion.
                var isLegacy = _launchData.MainReplay.ReplayData is GenericReplayData replayData &&
                    replayData.RecorderVersion != null &&
                    Version.TryParse(replayData.RecorderVersion, out var version) &&
                    version.CompareTo(new Version(0, 9, 5)) < 0;
                _usesLegacyPseudoLocalPoses = isLegacy;
                return isLegacy;
            }
        }

        private Vector3 _posOffset;
        private Quaternion _rotOffset;

        private void Awake() {
            this.LoadResources();
            ReplayerCore.SetParent(ReplayPoseOrigin, false);
            Plugin.Log.Notice($"[Replayer] Pose origin: {ReplayPoseOrigin.name}");
            name = "ReplayerCore";

            ReplayerCenterAdjust = new GameObject("CenterAdjust").transform;
            ReplayerCenterAdjust.SetParent(ReplayerCore, false);

            var settingsModel = _mainSystemInit._settingsManager.settings;
            _posOffset = settingsModel.room.center;
            _rotOffset = Quaternion.Euler(0, settingsModel.room.rotation, 0);
        }

        private void Start() {
            var poseOrigin = ReplayPoseOrigin;
            if (ReplayerCore.parent != poseOrigin)
                ReplayerCore.SetParent(poseOrigin, false);
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
