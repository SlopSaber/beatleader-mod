using System;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace BeatLeader.Replayer.Binding {
    public class HotkeysHandler : MonoBehaviour {
        [Inject] private readonly DiContainer _container = null!;
        private bool _loggedFirstUpdate;

        public IList<GameHotkey> Hotkeys { get; } = new List<GameHotkey> {
            new LayoutEditorModeHotkey(),
            new HideCursorHotkey(),
            new PauseHotkey(),
            new ExitHotkey(),
            new RewindBackwardHotkey(),
            new RewindForwardHotkey()
        };

        private void Awake() {
            foreach (var item in Hotkeys) {
                _container.Inject(item);
            }
        }

        private void Update() {
            if (!_loggedFirstUpdate) {
                _loggedFirstUpdate = true;
                Plugin.Log.Notice($"[Replayer] HotkeysHandler running; focused={Application.isFocused}");
            }
            foreach (var item in Hotkeys) {
                try {
                    if (Input.GetKeyDown(item.Key))
                        item.OnKeyDown();
                    else if (Input.GetKeyUp(item.Key))
                        item.OnKeyUp();
                } catch (Exception ex) {
                    Plugin.Log.Error($"[HotkeysHandler] Error during attempting to perform {item.GetType().Name} hotkey!\r\n{ex}");
                }
            }
        }
    }
}
