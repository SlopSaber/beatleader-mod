using BeatLeader.UI.Reactive;
using HMUI;
using Reactive;
using Reactive.Components;
using Reactive.Yoga;
using UnityEngine;
using Zenject;

namespace BeatLeader.UI.Hub {
    internal class BattleRoyaleOpponentsViewController : ViewController {
        #region Injection

        [Inject] private readonly IBattleRoyaleHost _battleRoyaleHost = null!;

        #endregion

        #region Construct

        private BattleRoyaleOpponentsList _opponentsList = null!;
        private CanvasGroup _localCanvasGroup = null!;

        private void Awake() {
            new Layout {
                Children = {
                    //header
                    new BattleRoyaleViewHeader {
                        Text = "Battle Opponents"
                    },
                    //list
                    new BattleRoyaleOpponentsList()
                        .AsFlexItem(flexGrow: 1f, margin: new() { top = 10f })
                        .Bind(ref _opponentsList)
                }
            }.AsFlexGroup(direction: FlexDirection.Column).WithNativeComponent(out _localCanvasGroup).Use(transform);

            _battleRoyaleHost.ReplayAddedEvent += HandleReplayAdded;
            _battleRoyaleHost.ReplayRemovedEvent += HandleReplayRemoved;
            _opponentsList.Setup(_battleRoyaleHost);

            _alphaAnimator.SetValueImmediate(0f);
            _alphaAnimator.Value = 0f;
        }

        public override void OnDestroy() {
            base.OnDestroy();
            _opponentsList.Setup(null);
            _battleRoyaleHost.ReplayAddedEvent -= HandleReplayAdded;
            _battleRoyaleHost.ReplayRemovedEvent -= HandleReplayRemoved;
        }

        #endregion

        #region Animation

        private readonly AnimatedValue<float> _alphaAnimator = new(0f, SingleValueInterpolator.Instance) { Duration = 20f.fact() };
        private int _replaysCount;
        private bool _isViewPresented;

        private void RefreshVisibility() {
            if (_replaysCount > 0 && !_isViewPresented) {
                PresentView();
            } else if (_replaysCount == 0 && _isViewPresented) {
                DismissView();
            }
        }

        private void PresentView() {
            _alphaAnimator.Value = 1f;
            _isViewPresented = true;
        }

        private void DismissView() {
            _alphaAnimator.Value = 0f;
            _isViewPresented = false;
        }

        private void Update() {
            ((IReactiveModule)_alphaAnimator).OnUpdate();
            _localCanvasGroup.alpha = _alphaAnimator.CurrentValue;
        }

        #endregion

        #region Callbacks

        private void HandleReplayAdded(BattleRoyaleReplay replay, object caller) {
            _replaysCount++;
            RefreshVisibility();
        }

        private void HandleReplayRemoved(BattleRoyaleReplay replay, object caller) {
            _replaysCount--;
            RefreshVisibility();
        }

        #endregion
    }
}