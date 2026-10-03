using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Interop;
using BeatLeader.Models;
using BeatLeader.Models.Replay;
using BeatLeader.Replayer;
using BeatLeader.Utils;
using HMUI;
using Zenject;

namespace BeatLeader {
    internal class HeckNavigationController : IInitializable, IDisposable, IReplayerViewNavigator {
        #region Injection

        [Inject] private readonly DiContainer _container = null!;
        [Inject] private readonly ReplayerMenuLoader _replayerMenuLoader = null!;
        [Inject] private readonly ReplayerViewNavigator _replayerViewNavigator = null!;
        [Inject] private readonly PlayerDataModel _playerDataModel = null!;

        #endregion

        #region Impl

        private sealed class ReplayNavigation {
            public readonly Replay Replay;
            public readonly Player Player;
            public readonly bool AlternativeLoading;
            public readonly ReplayerMenuLoader.LeaderboardReplaySelection? Selection;
            public readonly CancellationTokenSource Cancellation = new();
            public readonly CancellationToken Token;
            public int ActiveOperations = 1;
            public bool Retired;

            public ReplayNavigation(Replay replay, Player player, bool alternativeLoading) {
                Replay = replay;
                Player = player;
                AlternativeLoading = alternativeLoading;
                Selection = alternativeLoading ? ReplayerMenuLoader.LeaderboardReplaySelection.Capture() : null;
                Token = Cancellation.Token;
            }

            public void Retire() {
                if (Retired) return;
                Retired = true;
                try {
                    Cancellation.Cancel();
                } finally {
                    if (ActiveOperations == 0) Cancellation.Dispose();
                }
            }

            public void ReleaseOperation() {
                ActiveOperations--;
                if (Retired && ActiveOperations == 0) Cancellation.Dispose();
            }
        }

        private ReplayNavigation? _pendingNavigation;
        private ReplayNavigation? _presentedNavigation;
        private bool _disposed;

        public Task NavigateToReplayAsync(FlowCoordinator flowCoordinator, Replay replay, Player player, bool tryLoadSelectedMap) {
            return NavigateToReplayAsync(flowCoordinator, replay, player, tryLoadSelectedMap, CancellationToken.None);
        }

        internal async Task NavigateToReplayAsync(
            FlowCoordinator flowCoordinator, Replay replay, Player player, bool tryLoadSelectedMap, CancellationToken token
        ) {
            token.ThrowIfCancellationRequested();
            RetireNavigation();
            if (_disposed) throw new OperationCanceledException();
            var navigation = new ReplayNavigation(replay, player, tryLoadSelectedMap);
            _pendingNavigation = navigation;
            try {
                using var preparation = CancellationTokenSource.CreateLinkedTokenSource(token, navigation.Token);
                await Task.Yield();
                preparation.Token.ThrowIfCancellationRequested();
                EnsureCurrent(navigation);
                var info = replay.info;
                var level = await _replayerMenuLoader.LoadBeatmapAsync(
                    info.hash, info.mode, info.difficulty, preparation.Token);
                preparation.Token.ThrowIfCancellationRequested();
                EnsureCurrent(navigation);

                var startData = HeckInterop.CreateStartData(
                    info.mode,
                    level.Key,
                    level.Level,
                    null,
                    GameplayModifiers.noModifiers,
                    _playerDataModel.playerData.playerSpecificSettings
                );
                preparation.Token.ThrowIfCancellationRequested();
                EnsureCurrent(navigation);

                // Presenting configuration transfers ownership from the originating panel.
                HeckInitViewManager(flowCoordinator, startData, navigation);
            } catch {
                if (ReferenceEquals(_presentedNavigation, navigation)) {
                    _presentedNavigation = null;
                    _presentingHeck = false;
                    RestoreFlowCoordinator(_playViewManager);
                }
                if (ReferenceEquals(_pendingNavigation, navigation)) RetireNavigation();
                else navigation.Retire();
                throw;
            } finally {
                navigation.ReleaseOperation();
            }
        }

        private void EnsureCurrent(ReplayNavigation navigation) {
            navigation.Token.ThrowIfCancellationRequested();
            if (_disposed || !ReferenceEquals(_pendingNavigation, navigation)) throw new OperationCanceledException();
        }

        private void RetireNavigation() {
            var navigation = _pendingNavigation;
            _pendingNavigation = null;
            navigation?.Retire();
        }

        private async Task StartReplay(ReplayNavigation navigation) {
            navigation.ActiveOperations++;
            try {
                EnsureCurrent(navigation);
                if (navigation.AlternativeLoading) {
                    await _replayerMenuLoader.StartReplayFromLeaderboardAsync(
                        navigation.Replay, navigation.Player, navigation.Token, navigation.Selection);
                } else {
                    await _replayerMenuLoader.StartReplayAsync(navigation.Replay, navigation.Player, token: navigation.Token);
                }
            } catch (OperationCanceledException) when (navigation.Token.IsCancellationRequested || _disposed) {
            } finally {
                if (ReferenceEquals(_pendingNavigation, navigation)) RetireNavigation();
                else navigation.Retire();
                navigation.ReleaseOperation();
            }
        }

        #endregion

        #region Heck Reflection

        private static MethodInfo? _playViewDataOnPlayMethod;
        private static MethodInfo? _playViewManagerStartMethod;
        private static MethodInfo? _playViewManagerEarlyDismissMethod;
        private static MethodInfo? _playViewManagerActivateMethod;
        private static MethodInfo? _playViewManagerInitMethod;
        private static FieldInfo? _playViewManagerFlowCoordinatorField;
        private static FieldInfo? _playViewManagerViewControllersField;

        private HarmonyAutoPatch _playViewManagerStartPatch = null!;
        private HarmonyAutoPatch _playViewManagerDismissPatch = null!;
        private HarmonyAutoPatch _playViewManagerActivatePatch = null!;

        private static HeckNavigationController? _heckNavigationController;
        private static object? _originalFlowCoordinator;
        private static object? _customFlowCoordinator;
        private object _playViewManager = null!;
        private static bool _presentingHeck;
        private static bool _flowCoordinatorReplaced;

        public void Initialize() {
            var type = HeckInterop.PlayViewManagerType!;

            _playViewManagerStartMethod ??= type.GetMethodThrowable("StartStandard");
            _playViewManagerEarlyDismissMethod ??= type.GetMethodThrowable("EarlyDismiss");
            _playViewManagerInitMethod ??= type.GetMethodThrowable("Init");
            _playViewManagerActivateMethod ??= type.GetMethodThrowable("Activate");
            
            _playViewManagerFlowCoordinatorField ??= type.GetFieldThrowable("_flowCoordinator");
            _playViewManagerViewControllersField ??= type.GetFieldThrowable("_viewControllers");

            _playViewDataOnPlayMethod ??= HeckInterop.PlayViewControllerDataType!.GetMethodThrowable("InvokeOnPlay");

            _playViewManagerStartPatch = new HarmonyPatchDescriptor(
                _playViewManagerStartMethod,
                prefix: typeof(HeckNavigationController).GetMethodThrowable(nameof(HeckStartStandardPrefix))
            );

            _playViewManagerDismissPatch = new HarmonyPatchDescriptor(
                _playViewManagerEarlyDismissMethod,
                prefix: typeof(HeckNavigationController).GetMethodThrowable(nameof(HeckEarlyDismissPrefix)),
                postfix: typeof(HeckNavigationController).GetMethodThrowable(nameof(HeckEarlyDismissPostfix))
            );
            
            _playViewManagerActivatePatch = new HarmonyPatchDescriptor(
                _playViewManagerActivateMethod,
                prefix: typeof(HeckNavigationController).GetMethodThrowable(nameof(HeckActivatePrefix))
            );

            _playViewManager = _container.Resolve(HeckInterop.PlayViewManagerType);
            _heckNavigationController = this;
        }

        public void Dispose() {
            _disposed = true;
            RetireNavigation();
            _presentedNavigation?.Retire();
            _presentedNavigation = null;
            if (ReferenceEquals(_heckNavigationController, this) && _presentingHeck) {
                _presentingHeck = false;
                RestoreFlowCoordinator(_playViewManager);
            }
            _playViewManagerStartPatch.Dispose();
            _playViewManagerDismissPatch.Dispose();
            _playViewManagerActivatePatch.Dispose();
            if (ReferenceEquals(_heckNavigationController, this)) {
                _heckNavigationController = null;
                _originalFlowCoordinator = null;
                _customFlowCoordinator = null;
            }
        }

        private static void RestoreFlowCoordinator(object manager) {
            if (!_flowCoordinatorReplaced) return;
            _playViewManagerFlowCoordinatorField!.SetValue(manager, _originalFlowCoordinator);
            _flowCoordinatorReplaced = false;
            _originalFlowCoordinator = null;
            _customFlowCoordinator = null;
        }

        private void HeckInitViewManager(FlowCoordinator flowCoordinator, object data, ReplayNavigation navigation) {
            RestoreFlowCoordinator(_playViewManager);
            _presentingHeck = true;
            _presentedNavigation = navigation;
            _customFlowCoordinator = flowCoordinator;

            _playViewManagerInitMethod!.Invoke(_playViewManager, [data, false]);
        }

        private static void HeckActivatePrefix(object __instance) {
            if (!_presentingHeck || _heckNavigationController is not { } controller
                || !ReferenceEquals(controller._playViewManager, __instance)) {
                return;
            }
            
            if (!_flowCoordinatorReplaced) {
                _originalFlowCoordinator = _playViewManagerFlowCoordinatorField!.GetValue(__instance);
                _flowCoordinatorReplaced = true;
            }
            _playViewManagerFlowCoordinatorField!.SetValue(__instance, _customFlowCoordinator);
        }

        private static bool HeckStartStandardPrefix(object __instance) {
            if (!_presentingHeck || _heckNavigationController is not { } controller
                || !ReferenceEquals(controller._playViewManager, __instance)) {
                return true;
            }
            var navigation = controller._presentedNavigation;
            controller._presentedNavigation = null;
            
            _presentingHeck = false;
            RestoreFlowCoordinator(__instance);
            if (navigation == null || navigation.Retired || controller._disposed
                || !ReferenceEquals(controller._pendingNavigation, navigation)) return false;
            
            try {
                var viewControllers = (object[])_playViewManagerViewControllersField!.GetValue(__instance);
                foreach (var viewController in viewControllers) {
                    _playViewDataOnPlayMethod!.Invoke(viewController, []);
                    if (navigation.Retired || !ReferenceEquals(controller._pendingNavigation, navigation)) return false;
                }
                _ = controller.StartReplay(navigation).RunCatching();
                return false;
            } catch {
                if (ReferenceEquals(controller._pendingNavigation, navigation)) controller.RetireNavigation();
                else navigation.Retire();
                throw;
            }
        }

        private static void HeckEarlyDismissPrefix(object __instance, out ReplayNavigation? __state) {
            __state = _presentingHeck && _heckNavigationController is { } controller
                && ReferenceEquals(controller._playViewManager, __instance)
                ? controller._presentedNavigation : null;
        }

        private static void HeckEarlyDismissPostfix(object __instance, ReplayNavigation? __state) {
            if (!_presentingHeck || _heckNavigationController is not { } controller
                || !ReferenceEquals(controller._playViewManager, __instance) || __state == null
                || !ReferenceEquals(controller._presentedNavigation, __state)) return;
            _presentingHeck = false;
            controller._presentedNavigation = null;
            RestoreFlowCoordinator(__instance);
            if (ReferenceEquals(controller._pendingNavigation, __state)) controller.RetireNavigation();
            else __state.Retire();
        }

        #endregion

        #region Adapter

        public void NavigateToReplayManager(FlowCoordinator flowCoordinator, IReplayHeader header) {
            _replayerViewNavigator.NavigateToReplayManager(flowCoordinator, header);
        }

        public void NavigateToBattleRoyale(
            FlowCoordinator flowCoordinator,
            BeatmapLevelWithKey level,
            IReadOnlyCollection<IReplayHeader> plays,
            bool allowChanges,
            bool clearOnExit
        ) {
            _replayerViewNavigator.NavigateToBattleRoyale(flowCoordinator, level, plays, allowChanges, clearOnExit);
        }

        #endregion
    }
}
