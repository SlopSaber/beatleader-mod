using System.Collections.Generic;
using BeatLeader.Models;
using BeatLeader.Models.AbstractReplay;
using BeatLeader.Replayer.Emulation;
using BeatLeader.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;
using Reactive;
using Reactive.BeatSaber.Components;
using UnityEngine;

namespace BeatLeader.UI.Replayer {
    internal class PlayerList : ReactiveComponent {
        #region Setup

        private IBeatmapTimeController? _timeController;
        private IVirtualPlayersManager? _playersManager;
        private bool _destroyed;

        public void Setup(IEnumerable<IVirtualPlayer> players, IBeatmapTimeController? timeController, IVirtualPlayersManager? playersManager) {
            if (_playersManager != null) {
                _playersManager.PrimaryPlayerWasChangedEvent -= HandlePrimaryPlayerChanged;
            }
            _timeController = timeController;
            _playersManager = playersManager;
            if (_playersManager != null) {
                _playersManager.PrimaryPlayerWasChangedEvent += HandlePrimaryPlayerChanged;
            }
            ReloadCells(players);
            RefreshHandle();
        }

        protected override void OnLateUpdate() {
            UpdateHandleAnimation();
            UpdateCellsAnimation();
        }

        protected override void OnStart() {
            PlaceCells(false);
            PlaceHandle();
        }

        protected override void OnDisable() {
            InvalidateRanking();
            _cellUpdateRequired = _sortedCells.Count > 1;
        }

        protected override void OnDestroy() {
            _destroyed = true;
            InvalidateRanking();
            if (_playersManager != null) {
                _playersManager.PrimaryPlayerWasChangedEvent -= HandlePrimaryPlayerChanged;
            }
            DetachCells();
            _sortedCells.Clear();
            _playersManager = null;
            _timeController = null;
            _selectedPlayer = null;
        }

        #endregion

        #region Construct

        protected override void Construct(RectTransform rect) {
            rect.pivot = new(1f, 1f);
            // Handle
            new Image {
                ContentTransform = {
                    pivot = new(1f, 0.5f)
                },
                Sprite = BundleLoader.Sprites.triangleIcon,
                Material = BundleLoader.Materials.uiNoDepthMaterial,
                Color = new(0.1f, 0.1f, 0.1f)
            }.WithSizeDelta(4f, 6f).Bind(ref _handleTransform).Use(rect);
        }

        #endregion

        #region Handle

        private float HandlePos => _primaryCellPos - PlayerListCell.CELL_SIZE / 2f;
        private RectTransform _handleTransform = null!;

        private void RefreshHandle() {
            _handleTransform.gameObject.SetActive(_sortedCells.Count > 1);
        }

        private void PlaceHandle() {
            _handleTransform.localPosition = new(0f, HandlePos);
        }

        private void UpdateHandleAnimation() {
            _handleTransform.localPosition = Vector3.Lerp(
                _handleTransform.localPosition,
                new(0f, HandlePos),
                Time.deltaTime * 4f
            );
        }

        #endregion

        #region Cells

        private class CellComparator : IComparer<PlayerListCell> {
            public int Compare(PlayerListCell x, PlayerListCell y) {
                var xScore = GetScore(x.Player);
                var yScore = GetScore(y.Player);
                return Comparer<int>.Default.Compare(yScore, xScore);
            }

            private static int GetScore(IVirtualPlayer player) {
                return player.ReplayScoreEventsProcessor.CurrentScoreEvent?.Value.score ?? 0;
            }
        }

        private readonly ReactivePool<PlayerListCell> _cellsPool = new() { DetachOnDespawn = false };
        private readonly List<PlayerListCell> _sortedCells = new();
        private readonly CellComparator _cellComparator = new();
        private float _primaryCellPos;

        private void ReloadCells(IEnumerable<IVirtualPlayer> players) {
            DespawnCells();
            if (_timeController == null) {
                return;
            }
            
            foreach (var player in players) {
                var cell = _cellsPool.Spawn();
                var trans = cell.ContentTransform;
                
                trans.pivot = new(1f, 1f);
                trans.anchorMin = new(0f, 1f);
                trans.anchorMax = new(1f, 1f);
                
                cell.Setup(player, _timeController!, this);
                cell.Use(ContentTransform);
                cell.CellSelectedEvent += HandleCellSelected;
                
                _sortedCells.Add(cell);
            }
            
            PlaceCells(false);
        }

        private void DespawnCells() {
            InvalidateRanking();
            DetachCells();
            _sortedCells.Clear();
            _selectedPlayer = null;
            _primaryCellPos = 0f;
            _cellUpdateRequired = false;
            _cellsPool.DespawnAll();
        }

        private void DetachCells() {
            foreach (var cell in _cellsPool.SpawnedComponents) {
                cell.CellSelectedEvent -= HandleCellSelected;
                cell.ReleasePlayer();
            }
        }

        private void PlaceCells(bool animated) {
            _sortedCells.Sort(_cellComparator);
            ApplyCellPositions(animated);
        }

        private void ApplyCellPositions(bool animated, RankingJob? job = null) {
            for (var i = 0; i < _sortedCells.Count; i++) {
                if (job != null && !IsCurrentRanking(job)) return;
                var cell = _sortedCells[i];
                if (job != null && (cell.IsDestroyed || !cell.IsInitialized || !cell.Content)) {
                    _cellUpdateRequired = true;
                    return;
                }
                var trans = cell.ContentTransform;
                var pos = -i * PlayerListCell.CELL_SIZE;
               
                trans.SetSiblingIndex(i);
                if (animated) {
                    cell.MoveTo(pos);
                } else {
                    trans.localPosition = new(0f, pos);
                }
                
                var isPrimary = cell.Player == _playersManager!.PrimaryPlayer;
                if (job != null && !IsCurrentRanking(job)) return;
                if (isPrimary) {
                    _primaryCellPos = pos;
                }
            }
        }

        private int FindPlayerIndex(IVirtualPlayer player) {
            for (var i = 0; i < _sortedCells.Count; i++) {
                var cell = _sortedCells[i];
                
                if (cell.Player == player) {
                    return i;
                }
            }
            return -1;
        }

        #endregion

        #region Cell Animation

        private float _lastReportedTime;
        private bool _cellUpdateRequired;
        private long _rankingRevision;
        private RankingJob? _rankingJob;

        private sealed class RankingJob {
            public readonly PlayerListCell[] Cells;
            public readonly long Revision;
            public readonly CancellationTokenSource Cancellation = new();
            public readonly CancellationToken Token;

            public RankingJob(PlayerListCell[] cells, long revision) {
                Cells = cells;
                Revision = revision;
                Token = Cancellation.Token;
            }
        }

        private void InvalidateRanking() {
            _rankingRevision++;
            var job = _rankingJob;
            if (job != null && !job.Token.IsCancellationRequested) job.Cancellation.Cancel();
        }

        private bool IsCurrentRanking(RankingJob job) {
            return ReferenceEquals(_rankingJob, job) && job.Revision == _rankingRevision
                && !job.Token.IsCancellationRequested && !_destroyed && !IsDestroyed
                && IsInitialized && Content && Content.activeInHierarchy;
        }

        private bool TryCaptureRanking(out PlayerListCell[] cells, out int[] scores) {
            cells = _sortedCells.ToArray();
            scores = new int[cells.Length];
            for (var i = 0; i < cells.Length; i++) {
                var player = cells[i].Player;
                if (player.GetType() != typeof(VirtualPlayer)) return false;
                var processor = player.ReplayScoreEventsProcessor;
                if (processor.GetType() != typeof(ReplayScoreEventsProcessor)) return false;
                scores[i] = processor.CurrentScoreEvent?.Value.score ?? 0;
            }
            return true;
        }

        private static int[] PrepareRanking(int[] scores, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            var order = new List<int>(scores.Length);
            for (var i = 0; i < scores.Length; i++) order.Add(i);
            order.Sort(new ScoreIndexComparer(scores));
            token.ThrowIfCancellationRequested();
            return order.ToArray();
        }

        private sealed class ScoreIndexComparer : IComparer<int> {
            private readonly int[] _scores;

            public ScoreIndexComparer(int[] scores) {
                _scores = scores;
            }

            public int Compare(int left, int right) => Comparer<int>.Default.Compare(_scores[right], _scores[left]);
        }

        private async Task RankCellsAsync(RankingJob job, int[] scores) {
            var token = job.Token;
            try {
                var order = await Task.Run(() => PrepareRanking(scores, token), token);
                if (!IsCurrentRanking(job)) {
                    if (!_destroyed && job.Revision == _rankingRevision) _cellUpdateRequired = true;
                    return;
                }
                _sortedCells.Clear();
                foreach (var index in order) _sortedCells.Add(job.Cells[index]);
                ApplyCellPositions(true, job);
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            } catch (Exception error) {
                Plugin.Log.Error(error);
                if (!_destroyed && job.Revision == _rankingRevision) _cellUpdateRequired = true;
            } finally {
                if (ReferenceEquals(_rankingJob, job)) _rankingJob = null;
                job.Cancellation.Dispose();
            }
        }

        public void NotifyCellUpdateRequired() {
            _cellUpdateRequired = true;
        }

        private void UpdateCellsAnimation() {
            if (_rankingJob != null) return;
            var time = Time.time;
            
            if (_cellUpdateRequired && time - _lastReportedTime > 0.5f) {
                if (_sortedCells.Count < 2 || !TryCaptureRanking(out var cells, out var scores)) {
                    PlaceCells(true);
                    _lastReportedTime = time;
                    _cellUpdateRequired = false;
                    return;
                }
                _lastReportedTime = time;
                _cellUpdateRequired = false;
                var job = new RankingJob(cells, _rankingRevision);
                _rankingJob = job;
                _ = RankCellsAsync(job, scores).RunCatching();
            }
        }

        #endregion

        #region Callbacks

        private IVirtualPlayer? _selectedPlayer;
        
        private void HandleCellSelected(PlayerListCell cell) {
            if (_destroyed || !_sortedCells.Contains(cell)) return;
            var player = cell.Player;
            if (player == _selectedPlayer) {
                return;
            }
            
            var revision = _rankingRevision;
            _playersManager?.SetPrimaryPlayer(player);
            if (!_destroyed && revision == _rankingRevision && _sortedCells.Contains(cell)) {
                _selectedPlayer = player;
            }
        }

        private void HandlePrimaryPlayerChanged(IVirtualPlayer player) {
            if (_destroyed || !IsInitialized || !Content) return;
            var index = FindPlayerIndex(player);
            _primaryCellPos = -index * PlayerListCell.CELL_SIZE;
            RefreshHandle();
        }

        #endregion
    }
}
