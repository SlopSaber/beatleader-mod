using System;
using System.Linq;
using System.Threading.Tasks;
using BeatLeader.Utils;
using BeatSaber.AvatarCore;
using BeatSaber.BeatAvatarAdapter;
using BeatSaber.BeatAvatarAdapter.AvatarEditor;
using UnityEngine;
using Zenject;

namespace BeatLeader.Replayer.Emulation {
    public class BeatAvatarLoader : MonoBehaviour {

        #region Setup

        [Inject] private readonly AvatarSystemCollection _avatarSystemCollection = null!;
        [Inject] private readonly DiContainer _container = null!;

        private GameObject AvatarPrefab {
            get {
                if (!_avatarPrefab) {
                    _avatarPrefab = FindAvatarPrefab();
                }
                return _avatarPrefab;
            }
        }

        private static GameObject FindAvatarPrefab() {
            var controller = Resources.FindObjectsOfTypeAll<AvatarTweenController>()
                .FirstOrDefault(static x => x && x.name == "AnimatedAvatar");
            return controller != null ? controller.gameObject
                : throw new InvalidOperationException("Avatar assets are not ready. Await CreateEditorFlowCoordinator before creating avatars.");
        }

        private IAvatarSystem _avatarSystem = null!;
        private Task<BeatAvatarEditorFlowCoordinator>? _editorTask;
        private GameObject _avatarPrefab = null!;

        private void Awake() {
            _avatarSystem = _avatarSystemCollection.availableAvatarSystems
                .Select(_avatarSystemCollection.GetAvatarSystem).OfType<BeatAvatarSystem>().First();
        }

        #endregion

        #region CreateAvatar

        public BeatAvatarController CreateGameplayAvatar(Transform? parent = null) {
            var avatar = CreateAvatar(parent, 1f);
            ApplyCam2Shenanigans(avatar);

            avatar.GetComponent<Animator>().enabled = false;
            return avatar.AddComponent<BeatAvatarController>();
        }

        public MenuBeatAvatarController CreateMenuAvatar(Transform? parent = null, float size = 1.2f) {
            var avatar = CreateAvatar(parent, size);
            return avatar.AddComponent<MenuBeatAvatarController>();
        }

        private GameObject CreateAvatar(Transform? parent, float size) {
            var avatar = Instantiate(AvatarPrefab, parent, false);
            var trans = avatar.transform;

            foreach (Transform child in trans) {
                child.localScale = Vector3.one;
            }

            trans.localPosition = Vector3.zero;
            trans.localScale = size * Vector3.one;
            trans.localRotation = Quaternion.identity;
            _container.InjectGameObject(avatar);

            // Forcibly enable to initiate Awake
            avatar.SetActive(true);
            avatar.name = "AnimatedAvatar (BL)";

            return avatar;
        }

        private static readonly int cullModeProp = Shader.PropertyToID("_CullMode");
        
        private static void ApplyCam2Shenanigans(GameObject avatar) {
            // For camera2 support
            foreach (var item in avatar.transform.GetChildren(false)) {
                if (item.name == "Eyes" && item.GetComponent<SpriteRenderer>() is { } renderer) {
                    renderer.material.SetInt(cullModeProp, 2);
                }
                
                item.gameObject.layer = 10;
            }
        }

        #endregion

        #region CreateEditor

        public Task<BeatAvatarEditorFlowCoordinator> CreateEditorFlowCoordinator() {
            if (_editorTask == null || _editorTask.IsFaulted || _editorTask.IsCanceled) {
                _editorTask = LoadEditorAndAvatarPrefab();
            }
            return _editorTask;
        }

        private async Task<BeatAvatarEditorFlowCoordinator> LoadEditorAndAvatarPrefab() {
            var editor = (BeatAvatarEditorFlowCoordinator)await _avatarSystem.InstantiateAvatarEditorUI(_container);
            try {
                _avatarPrefab = FindAvatarPrefab();
                return editor;
            } catch {
                Destroy(editor.gameObject);
                throw;
            }
        }

        #endregion
    }
}