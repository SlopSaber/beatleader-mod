using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BeatLeader {
    public class AccuracyGraphLine : Graphic {
        #region Serialized

        [SerializeField] private int resolution = 500;
        [SerializeField] private float thickness = 0.2f;

        #endregion

        #region Mesh settings

        private GraphMeshHelper? _graphMeshHelper;

        internal (int Resolution, float Thickness) CaptureMeshSettings() {
            return (resolution, thickness);
        }

        protected override void Start() {
            base.Start();
        }

        #endregion

        #region OnPopulateMesh

        protected override void OnPopulateMesh(VertexHelper vh) {
            if (_graphMeshHelper == null && _points == null) {
                vh.Clear();
                return;
            }
            var screenRect = RectTransformUtility.PixelAdjustRect(rectTransform, canvas);
            var screenViewTransform = new ScreenViewTransform(screenRect, _viewRect);

            _graphMeshHelper ??= new GraphMeshHelper(resolution, 1, thickness);
            if (_points != null) {
                _graphMeshHelper.SetPoints(_points);
            }
            _graphMeshHelper.PopulateMesh(vh, screenViewTransform, _canvasRadius);
        }

        #endregion

        #region Setup

        private List<Vector2>? _points;
        private float _canvasRadius;
        private Rect _viewRect = Rect.MinMaxRect(0, 0, 1, 1);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0016", Justification = "Retain the concrete collection type in this published API for binary compatibility.")]
        public void Setup(List<Vector2> points, Rect viewRect, float canvasRadius) {
            _points = points;
            _viewRect = viewRect;
            _canvasRadius = canvasRadius;

            SetVerticesDirty();
        }

        internal void SetupPrepared(GraphMeshHelper mesh, Rect viewRect, float canvasRadius) {
            _points = null;
            _graphMeshHelper = mesh;
            _viewRect = viewRect;
            _canvasRadius = canvasRadius;
            SetVerticesDirty();
        }

        #endregion
    }
}
