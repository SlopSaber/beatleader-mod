using BeatLeader.Models;
using HMUI;
using UnityEngine.Rendering;
using Zenject;

namespace BeatLeader.UI.Replayer.Desktop {
    internal class ReplayerDesktopUIBinder : ReplayerUIBinder {
        [Inject] private readonly ReplayerDesktopScreenSystem _screenSystem = null!;
        [Inject] private readonly DiContainer _container = null!;
        [Inject] private readonly ReplayerDesktopViewController _viewController = null!;
        [Inject] private readonly ReplayLaunchData _launchData = null!;

        protected override void SetUIEnabled(bool uiEnabled) {
            _screenSystem.SetUIEnabled(uiEnabled);
        }

        protected override void SetupUI() {
            if (_launchData.Settings.UISettings.ShowUIOnPause) {
                _screenSystem.ShowImmediate();
            }
            _screenSystem.Screen.SetRootViewController(_viewController, ViewController.AnimationType.None);
            if (GraphicsSettings.currentRenderPipeline != null) {
                _screenSystem.SetOverlayMode();
            } else {
                _screenSystem.SetRenderCamera(_container.Resolve<ReplayerDesktopUIRenderer>().RenderCamera);
            }
        }
    }
}
