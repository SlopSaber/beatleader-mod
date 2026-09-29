using BeatLeader.Models;
using UnityEngine;
using Zenject;

namespace BeatLeader.Replayer.Binding
{
    internal class PauseHotkey : GameHotkey
    {
        public override KeyCode Key => _launchData.Settings.Shortcuts.PauseHotkey;

        [Inject] private readonly IReplayPauseController _playbackController = default!;
        [Inject] private readonly ReplayLaunchData _launchData = default!;

        public override void OnKeyDown()
        {
            if (!_playbackController.IsPaused)
                _playbackController.Pause();
            else
                _playbackController.Resume();
        }
    }
}
