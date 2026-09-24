using BeatLeader.Utils;
using BeatLeader.Models;
using System.Linq;
using UnityEngine;
using VRUIControls;
using Zenject;

namespace BeatLeader.Replayer.Tweaking {
    internal class GarbageDisablerTweak : GameTweak {
        [Inject] private readonly MainCamera _mainCamera;
        [Inject] private readonly ICameraController _cameraController;

        [FirstResource(RequireActiveInHierarchy = true)] 
        private readonly SaberBurnMarkArea _burnMarkArea;
        
        [FirstResource] 
        private readonly VRLaserPointer _pointer;

        private AudioListener _replayAudioListener;
        private bool _createdReplayAudioListener;

        public override void Initialize() {
            this.LoadResources();

            _pointer.gameObject.SetActive(!InputUtils.UsesFPFC);
            _burnMarkArea.gameObject.SetActive(!InputUtils.UsesFPFC);
            _mainCamera.gameObject.SetActive(false);

            if (!Resources.FindObjectsOfTypeAll<AudioListener>().Any(listener => listener.isActiveAndEnabled)) {
                var replayCamera = _cameraController.Camera;
                if (replayCamera != null) {
                    _replayAudioListener = replayCamera.GetComponent<AudioListener>();
                    if (_replayAudioListener == null) {
                        _replayAudioListener = replayCamera.gameObject.AddComponent<AudioListener>();
                        _createdReplayAudioListener = true;
                    }
                    _replayAudioListener.enabled = true;
                    Plugin.Log.Notice("[Replayer] Restored audio listener on replay camera");
                }
            }
        }
        public override void Dispose() {
            if (_replayAudioListener != null) {
                _replayAudioListener.enabled = false;
                if (_createdReplayAudioListener) Object.Destroy(_replayAudioListener);
            }

            if (_pointer != null)
                _pointer.gameObject.SetActive(true);

            if (_burnMarkArea != null)
                _burnMarkArea.gameObject.SetActive(true);

            if (_mainCamera != null)
                _mainCamera.gameObject.SetActive(true);
        }
    }
}
