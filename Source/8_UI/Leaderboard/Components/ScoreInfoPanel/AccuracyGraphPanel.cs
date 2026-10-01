using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Models;
using BeatSaberMarkupLanguage.Attributes;
using HMUI;
using JetBrains.Annotations;
using UnityEngine;
using VRUIControls;
using Object = UnityEngine.Object;
using Vector3 = UnityEngine.Vector3;

namespace BeatLeader.Components {
    internal class AccuracyGraphPanel : ReeUIComponentV2 {
        #region Initialize

        private AccuracyGraph _accuracyGraph;

        protected override void OnInitialize() {
            InvalidatePreparation();
            var go = Object.Instantiate(BundleLoader.AccuracyGraphPrefab, _graphContainer, false);
            _accuracyGraph = go.GetComponent<AccuracyGraph>();
            _accuracyGraph.Construct(_graphBackground);
        }

        protected override void OnDispose() {
            InvalidatePreparation();
        }

        #endregion

        #region SetScoreStats

        private float[] _points = Array.Empty<float>();
        private float _songDuration = 1.0f;
        private Rect _viewRect = Rect.zero;
        private float[]? _sampleBounds;
        private float _sampleStep;
        private int _preparationRevision;
        private CancellationTokenSource? _preparationCancellation;

        public void SetScoreStats(ScoreStats scoreStats) {
            InvalidatePreparation();
            _sampleBounds = null;
            _songDuration = scoreStats.winTracker.endTime;
            _points = scoreStats.scoreGraphTracker.graph;

            AccuracyGraphUtils.PostProcessPoints(_points, out var positions, out _viewRect);
            _accuracyGraph.Setup(positions, _viewRect, GetCanvasRadius(), _songDuration);
        }

        internal void InvalidatePreparation() {
            _preparationRevision++;
            var previous = _preparationCancellation;
            _preparationCancellation = null;
            previous?.Cancel();
        }

        internal async Task<PreparedAccuracyGraph?> PrepareScoreStatsAsync(ScoreStats scoreStats, CancellationToken token = default) {
            if (!this || !IsHierarchySet || !Content || !_accuracyGraph) return null;
            InvalidatePreparation();
            var revision = _preparationRevision;
            var source = CancellationTokenSource.CreateLinkedTokenSource(token);
            _preparationCancellation = source;
            var preparationToken = source.Token;
            try {
                var settings = _accuracyGraph.CaptureMeshSettings();
                return await AccuracyGraphUtils.PrepareGraphAsync(scoreStats.scoreGraphTracker.graph,
                    scoreStats.winTracker.endTime, settings.Resolution, settings.Thickness, revision, preparationToken);
            } catch (OperationCanceledException) when (preparationToken.IsCancellationRequested) {
                return null;
            } finally {
                if (ReferenceEquals(_preparationCancellation, source)) _preparationCancellation = null;
                source.Dispose();
            }
        }

        internal bool CanApplyPrepared(PreparedAccuracyGraph prepared) {
            return prepared.Revision == _preparationRevision && this && IsHierarchySet && Content && _accuracyGraph;
        }

        internal bool SetPreparedScoreStats(PreparedAccuracyGraph prepared) {
            if (!CanApplyPrepared(prepared)) return false;
            _songDuration = prepared.SongDuration;
            _points = prepared.Points;
            _viewRect = prepared.ViewRect;
            _sampleBounds = prepared.SampleBounds;
            _sampleStep = prepared.SampleStep;
            _accuracyGraph.SetupPrepared(prepared, GetCanvasRadius());
            return true;
        }

        #endregion

        #region CursorPosition

        private readonly Vector3[] _corners = new Vector3[4];
        private VRPointer _vrPointer;
        private Vector3 _lastPosition3D;
        private bool _cursorInitialized;

        private void OnEnable() {
            // Tab switches reuse the same pointer. Search again only after its scene object is destroyed.
            if (_vrPointer == null) _vrPointer = FindFirstObjectByType<VRPointer>();
            _cursorInitialized = _vrPointer != null;
            _lastPosition3D = default;
        }

        private void LateUpdate() {
            if (!_graphContainer.gameObject.activeInHierarchy || !_cursorInitialized) return;

            var cursorPosition3D = _vrPointer.cursorPosition;
            if (cursorPosition3D.Equals(_lastPosition3D)) return;
            _lastPosition3D = cursorPosition3D;

            CalculateCursorPosition(cursorPosition3D, out var normalized);
            UpdateCursor(normalized);
        }

        private void CalculateCursorPosition(Vector3 worldCursor, out Vector2 normalized) {
            var canvasRadius = GetCanvasRadius() * _graphContainer.lossyScale.x;
            var nonCurved = AccuracyGraphUtils.TransformPointFrom3DToCanvas(worldCursor, canvasRadius);

            _graphContainer.GetWorldCorners(_corners);

            normalized = new Vector2(
                new Range(_corners[0].x, _corners[3].x).GetRatio(nonCurved.x),
                new Range(_corners[0].y, _corners[1].y).GetRatio(nonCurved.y)
            );
        }

        #endregion

        #region UpdateCursor

        private float _targetViewTime;
        private float _currentViewTime;

        private void UpdateCursor(Vector2 normalized) {
            if (normalized.x < 0 || normalized.y < 0 || normalized.x > 1 || normalized.y > 1) return;
            var viewCursor = Rect.NormalizedToPoint(_viewRect, normalized);
            _targetViewTime = Mathf.Clamp01(viewCursor.x);
        }

        private void Update() {
            if (!_graphContainer.gameObject.activeInHierarchy || float.IsNaN(_targetViewTime)) return;
            _currentViewTime = Mathf.Lerp(_currentViewTime, _targetViewTime, Time.deltaTime * 10.0f);
            var songTime = _currentViewTime * _songDuration;
            var accuracy = GetAccuracy(_currentViewTime);
            _accuracyGraph.SetCursor(_currentViewTime);
            CursorText = FormatCursorText(songTime, accuracy);
        }

        private static string FormatCursorText(float songTime, float accuracy) {
            var fullMinutes = Mathf.FloorToInt(songTime / 60.0f);
            var remainingSeconds = Mathf.FloorToInt(Mathf.Abs(songTime % 60.0f));
            return $"<color=#B856FF><bll>ls-song-time</bll>: </color>{fullMinutes}:{remainingSeconds:00}  <color=#B856FF><bll>ls-accuracy</bll>: </color>{accuracy * 100.0f:F2}<size=70%>%";
        }

        private float GetAccuracy(float viewTime) {
            if (_points.Length == 0) return 1.0f;

            if (_sampleBounds is { } bounds) {
                var low = 0;
                var high = bounds.Length;
                while (low < high) {
                    var middle = low + (high - low) / 2;
                    if (bounds[middle] < viewTime) low = middle + 1;
                    else high = middle;
                }
                if (low == bounds.Length) return _points[_points.Length - 1];
                // These bounds use the legacy incremental float step, including its rounding.
                var upper = bounds[low];
                var lower = upper - _sampleStep;
                var ratio = (viewTime - lower) / (upper - lower);
                return _points[low] + (_points[low + 1] - _points[low]) * ratio;
            }

            var xStep = 1.0f / _points.Length;
            var x = xStep;

            for (var i = 1; i < _points.Length; i++, x += xStep) {
                if (x < viewTime) continue;
                var xRange = new Range(x - xStep, x);
                var yRange = new Range(_points[i - 1], _points[i]);
                var ratio = xRange.GetRatio(viewTime);
                return yRange.SlideBy(ratio);
            }

            return _points.Last();
        }

        #endregion

        #region GetCanvasRadius

        private readonly CurvedCanvasSettingsHelper _curvedCanvasSettingsHelper = new();

        private float GetCanvasRadius() {
            var canvasSettings = _curvedCanvasSettingsHelper.GetCurvedCanvasSettings(_accuracyGraph.Canvas);
            return canvasSettings == null ? float.MaxValue : canvasSettings.radius;
        }

        #endregion

        #region SetActive

        public void SetActive(bool value) {
            Active = value;
        }

        #endregion

        #region Active

        private bool _active = true;

        [UIValue("active"), UsedImplicitly]
        private bool Active {
            get => _active;
            set {
                if (_active.Equals(value)) return;
                _active = value;
                NotifyPropertyChanged();
            }
        }

        #endregion

        #region UIComponents

        [UIComponent("graph-container"), UsedImplicitly]
        private RectTransform _graphContainer = default!;

        [UIComponent("graph-container"), UsedImplicitly]
        private ImageView _graphBackground = default!;

        [UIComponent("cursor-hint"), UsedImplicitly]
        private RectTransform _hintTransform { get; set; } = null!;

        #endregion

        #region CursorText

        private string _cursorText = "";

        [UIValue("cursor-text"), UsedImplicitly]
        private string CursorText {
            get => _cursorText;
            set {
                if (_cursorText.Equals(value)) return;
                _cursorText = value;
                NotifyPropertyChanged();
            }
        }

        #endregion
    }
}
