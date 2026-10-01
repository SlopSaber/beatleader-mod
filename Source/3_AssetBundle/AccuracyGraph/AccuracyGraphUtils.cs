using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BeatLeader {
    internal static class AccuracyGraphUtils {
        #region PostProcessPoints

        private const float MinimalXForScaling = 0.05f;

        public static void PostProcessPoints(float[] points, out List<Vector2> positions, out Rect viewRect) {
            PostProcessPoints(points, CancellationToken.None, out positions, out viewRect);
        }

        private static void PostProcessPoints(float[] points, CancellationToken token, out List<Vector2> positions, out Rect viewRect) {
            positions = new List<Vector2>();

            var yMin = float.MaxValue;
            var yMax = float.MinValue;

            for (var i = 0; i < points.Length; i++) {
                token.ThrowIfCancellationRequested();
                var x = (float) i / (points.Length - 1);
                var y = points[i];
                positions.Add(new Vector2(x, y));
                if (x < MinimalXForScaling) continue;
                if (y > yMax) yMax = y;
                if (y < yMin) yMin = y;
            }

            var margin = (yMax - yMin) * 0.2f;
            viewRect = Rect.MinMaxRect(-0.04f, yMin - margin, 1.04f, yMax + margin);

            ReducePositionsList(positions, viewRect, token);
        }

        internal static Task<PreparedAccuracyGraph> PrepareGraphAsync(float[] points, float songDuration,
            int resolution, float thickness, int revision, CancellationToken token) {
            // The published stats array remains caller-owned; only this copy enters the worker.
            var snapshot = (float[])points.Clone();
            return Task.Run(() => {
                PostProcessPoints(snapshot, token, out var positions, out var viewRect);
                var mesh = new GraphMeshHelper(resolution, 1, thickness);
                mesh.SetPoints(positions, token);
                mesh.PreparePoints(token);

                var step = 1.0f / snapshot.Length;
                var bounds = snapshot.Length > 1 ? new float[snapshot.Length - 1] : Array.Empty<float>();
                var x = step;
                for (var i = 0; i < bounds.Length; i++, x += step) {
                    token.ThrowIfCancellationRequested();
                    bounds[i] = x;
                }
                return new PreparedAccuracyGraph(revision, songDuration, snapshot, viewRect, mesh, bounds, step);
            }, token);
        }

        #endregion

        #region ReducePositionsList

        private const float ReduceAngleMargin = 10f;
        private const float ReduceProximityMargin = 0.1f;

        private static void ReducePositionsList(IList<Vector2> positions, Rect viewRect, CancellationToken token) {
            var startIndex = 1;
            while (startIndex < positions.Count - 1) {
                var i = startIndex;
                for (; i < positions.Count - 1; i++) {
                    token.ThrowIfCancellationRequested();
                    var prev = Rect.PointToNormalized(viewRect, positions[i - 1]);
                    var curr = Rect.PointToNormalized(viewRect, positions[i]);
                    var next = Rect.PointToNormalized(viewRect, positions[i + 1]);

                    var a = next - curr;
                    var b = curr - prev;
                    if (a.magnitude > ReduceProximityMargin || b.magnitude > ReduceProximityMargin) continue;
                    if (Vector2.Angle(a, b) > ReduceAngleMargin) continue;
                    positions.RemoveAt(i);
                    break;
                }

                startIndex = i;
            }
        }

        #endregion

        #region TransformPointFrom3DToCanvas

        public static Vector2 TransformPointFrom3DToCanvas(Vector3 point, float canvasRadius) {
            if (canvasRadius < 1e-10f) return point;
            var x = Mathf.Asin(point.x / canvasRadius) * canvasRadius;
            return new Vector2(x, point.y);
        }

        #endregion
    }

    internal sealed class PreparedAccuracyGraph {
        internal readonly int Revision;
        internal readonly float SongDuration;
        internal readonly float[] Points;
        internal readonly Rect ViewRect;
        internal readonly GraphMeshHelper Mesh;
        internal readonly float[] SampleBounds;
        internal readonly float SampleStep;

        internal PreparedAccuracyGraph(int revision, float songDuration, float[] points, Rect viewRect,
            GraphMeshHelper mesh, float[] sampleBounds, float sampleStep) {
            Revision = revision;
            SongDuration = songDuration;
            Points = points;
            ViewRect = viewRect;
            Mesh = mesh;
            SampleBounds = sampleBounds;
            SampleStep = sampleStep;
        }
    }
}
