using BeatLeader.Models;
using BeatLeader.Utils;
using JetBrains.Annotations;
using UnityEngine;
using Zenject;

namespace BeatLeader.Replayer.Emulation {
    internal class VirtualPlayerGameSabers : IVirtualPlayerBody {
        [Inject]
        private readonly PlayerVRControllersManager _vrControllersManager = null!;
        
        private Transform _leftController = null!;
        private Transform _rightController = null!;
#if BEAT_SABER_1453
        private BasicBodySettings? _settings;
#endif

        [Inject, UsedImplicitly]
        private void Init() {
            _leftController = _vrControllersManager.leftHandVRController.transform;
            _rightController = _vrControllersManager.rightHandVRController.transform;

            _vrControllersManager.GetComponent<DeactivateVRControllersOnFocusCapture>().enabled = false;
            _vrControllersManager.SetupAutoplayForAllControllers();
        }

        public void ApplySettings(BasicBodySettings basicBodySettings) {
#if BEAT_SABER_1453
            _settings = basicBodySettings;
#endif
            _leftController.gameObject.SetActive(basicBodySettings.LeftSaberEnabled);
            _rightController.gameObject.SetActive(basicBodySettings.RightSaberEnabled);
        }

#if BEAT_SABER_1453
        internal void RestoreReplayControl() {
            _vrControllersManager.SetupAutoplayForAllControllers();
            if (_settings != null) ApplySettings(_settings);
            Plugin.Log.Notice($"[Replayer] Saber visibility: left={_leftController.gameObject.activeInHierarchy}, right={_rightController.gameObject.activeInHierarchy}; tracking left={_vrControllersManager.leftHandVRController.enabled}, right={_vrControllersManager.rightHandVRController.enabled}.");
        }
#endif

        public void ApplyPose(Pose headPose, Pose leftHandPose, Pose rightHandPose) {
            _leftController.SetLocalPose(leftHandPose);
            _rightController.SetLocalPose(rightHandPose);
        }
    }
}
