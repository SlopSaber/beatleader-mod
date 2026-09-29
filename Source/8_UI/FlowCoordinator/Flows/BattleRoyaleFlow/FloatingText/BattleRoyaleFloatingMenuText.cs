using BeatLeader.UI.Reactive;
using BeatLeader.UI.Reactive.Components;
using HMUI;
using Reactive;
using Reactive.BeatSaber;
using Reactive.BeatSaber.Components;
using Reactive.Components;
using UnityEngine;

namespace BeatLeader.UI.Hub {
    internal class BattleRoyaleFloatingMenuText : ReactiveComponent {
        #region Animation

        private readonly AnimatedValue<float> _animator = new(0f, SingleValueInterpolator.Instance) { Duration = 15f.fact() };

        public void Present() {
            _animator.Value = 1f;
        }

        public void Hide() {
            _animator.Value = 0f;
        }

        protected override void OnUpdate() {
            ((IReactiveModule)_animator).OnUpdate();
            _canvasGroup.alpha = _animator.CurrentValue;
        }

        #endregion

        #region Construct

        private CanvasGroup _canvasGroup = null!;

        protected override GameObject Construct() {
            return new Layout {
                Children = {
                    new Label {
                        Text = "Did you see it?! Monke stole all players and mystically disappeared!\n" +
                            "But don't be upset, add some by yourself!",
                        Color = UIStyle.SecondaryTextColor,
                        FontSize = 6f
                    }
                }
            }.WithNativeComponent(out _canvasGroup).Use();
        }

        protected override void OnInitialize() {
            BeatSaberUtils.AddCanvas(this);
            
            Content.AddComponent<CurvedCanvasSettings>();
            ContentTransform.localScale = Vector3.one * 0.02f;
            
            var floating = Content.AddComponent<FloatingObject>();
            floating.amplitude = 0.04f;
            floating.speed = 0.8f;
            floating.rotationAmplitude = 5f;
            floating.rotationSpeed = 1f;
        }

        #endregion
    }
}