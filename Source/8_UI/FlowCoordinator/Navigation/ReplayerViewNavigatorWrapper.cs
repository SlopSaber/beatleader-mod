using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Models;
using BeatLeader.Models.Replay;
using HMUI;
using JetBrains.Annotations;

namespace BeatLeader {
    /// <summary>
    /// Used to wrap IReplayerViewNavigator and avoid providing FlowCoordinator each time.
    /// </summary>
    [PublicAPI]
    public class ReplayerViewNavigatorWrapper {
        public ReplayerViewNavigatorWrapper(IReplayerViewNavigator replayerViewNavigator, FlowCoordinator flowCoordinator) {
            _viewNavigator = replayerViewNavigator;
            _flowCoordinator = flowCoordinator;
        }

        private readonly IReplayerViewNavigator _viewNavigator;
        private readonly FlowCoordinator _flowCoordinator;

        /// <inheritdoc cref="IReplayerViewNavigator.NavigateToReplayAsync"/>
        public Task NavigateToReplayAsync(Replay replay, Player player, bool tryLoadSelectedMap) {
            return _viewNavigator.NavigateToReplayAsync(_flowCoordinator, replay, player, tryLoadSelectedMap);
        }

        internal Task NavigateToReplayAsync(Replay replay, Player player, bool tryLoadSelectedMap, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            return _viewNavigator switch {
                ReplayerViewNavigator navigator => navigator.NavigateToReplayAsync(_flowCoordinator, replay, player, tryLoadSelectedMap, token),
                HeckNavigationController navigator => navigator.NavigateToReplayAsync(_flowCoordinator, replay, player, tryLoadSelectedMap, token),
                _ => _viewNavigator.NavigateToReplayAsync(_flowCoordinator, replay, player, tryLoadSelectedMap)
            };
        }

        /// <inheritdoc cref="IReplayerViewNavigator.NavigateToReplayManager"/>
        public void NavigateToReplayManager(IReplayHeader header) {
            _viewNavigator.NavigateToReplayManager(_flowCoordinator, header);
        }

        /// <inheritdoc cref="IReplayerViewNavigator.NavigateToBattleRoyale"/>
        public void NavigateToBattleRoyale(BeatmapLevelWithKey level, IReadOnlyCollection<IReplayHeader> plays, bool allowChanges, bool clearOnExit) {
            _viewNavigator.NavigateToBattleRoyale(_flowCoordinator, level, plays, allowChanges, clearOnExit);
        }
    }
}
