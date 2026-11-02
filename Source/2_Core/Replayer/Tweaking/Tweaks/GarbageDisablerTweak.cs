using BeatLeader.Utils;
using BeatLeader.Models;
using System;
using System.Linq;
using UnityEngine;
using VRUIControls;
using Zenject;

namespace BeatLeader.Replayer.Tweaking {
    internal class GarbageDisablerTweak : GameTweak {
        [Inject] private readonly MainCamera _mainCamera;
        [Inject] private readonly ICameraController _cameraController;
        [Inject] private readonly ReplayerExtraObjectsProvider _extraObjects;

        [FirstResource(RequireActiveInHierarchy = true)] 
        private readonly SaberBurnMarkArea _burnMarkArea;
        
        [FirstResource] 
        private readonly VRLaserPointer _pointer;

        private AudioListener[] _disabledAudioListeners = Array.Empty<AudioListener>();
        private AudioListener _replayAudioListener;
        private GameObject _replayAudioListenerObject;
        private bool _replayAudioListenerWasEnabled;
        private bool _createdReplayAudioListener;

        public override void Initialize() {
            this.LoadResources();

            _pointer.gameObject.SetActive(!InputUtils.UsesFPFC);
            _burnMarkArea.gameObject.SetActive(!InputUtils.UsesFPFC);
            _mainCamera.gameObject.SetActive(false);

            var replayCamera = _cameraController.Camera;
            _disabledAudioListeners = Resources.FindObjectsOfTypeAll<AudioListener>()
                .Where(listener => listener.isActiveAndEnabled && (replayCamera == null || listener.gameObject != replayCamera.gameObject))
                .ToArray();
            foreach (var listener in _disabledAudioListeners) listener.enabled = false;

            if (replayCamera != null) {
                _replayAudioListener = replayCamera.GetComponent<AudioListener>();
                if (_replayAudioListener == null) {
                    _replayAudioListener = replayCamera.gameObject.AddComponent<AudioListener>();
                    _createdReplayAudioListener = true;
                } else {
                    _replayAudioListenerWasEnabled = _replayAudioListener.enabled;
                }
                _replayAudioListener.enabled = true;
            } else {
                _replayAudioListenerObject = new GameObject("ReplayAudioListener");
                _replayAudioListenerObject.transform.SetParent(_extraObjects.ReplayerCore, false);
                _replayAudioListener = _replayAudioListenerObject.AddComponent<AudioListener>();
            }
            var listeners = Resources.FindObjectsOfTypeAll<AudioListener>()
                .Where(listener => listener.isActiveAndEnabled)
                .Select(listener => listener.name);
            Plugin.Log.Notice($"[Replayer] Audio listeners: {string.Join(", ", listeners)}; paused={AudioListener.pause}; volume={AudioListener.volume}");
        }
        public override void Dispose() {
            if (_replayAudioListener != null) {
                _replayAudioListener.enabled = _replayAudioListenerWasEnabled;
                if (_replayAudioListenerObject != null) UnityEngine.Object.Destroy(_replayAudioListenerObject);
                else if (_createdReplayAudioListener) UnityEngine.Object.Destroy(_replayAudioListener);
            }
            foreach (var listener in _disabledAudioListeners) {
                if (listener != null) listener.enabled = true;
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
