using BeatLeader.Models;
using UnityEngine;
using Zenject;

namespace BeatLeader.Replayer.Binding {
    internal class ExitHotkey : GameHotkey {
        [Inject] private readonly IReplayFinishController _finishController = null!;

        public override KeyCode Key => KeyCode.Escape;

        public override void OnKeyDown() {
            Plugin.Log.Notice("[Replayer] Escape pressed");
            _finishController.Exit();
        }
    }
}
