using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Models;

namespace BeatLeader {
    internal sealed class ScoreStatsPresentation {
        internal const int GridCellsCount = 12;
        private const string Neutral = "#FFFFFF";
        private const string Faded = "#888888";

        internal readonly struct ScoreText {
            internal readonly float Score;
            internal readonly string Plain;
            internal readonly string Hovered;

            internal ScoreText(float score, string format, IFormatProvider provider) {
                Score = score;
                Plain = score.ToString(format, provider);
                var accuracy = (score / 1.15f).ToString(format, provider);
                Hovered = $"<line-height=53%>{Plain}\n<size=80%>{accuracy}<size=50%>%";
            }
        }

        internal readonly struct Hand {
            internal readonly ScoreText Text;
            internal readonly float Fill;

            internal Hand(float score, IFormatProvider provider) {
                Text = new ScoreText(score, "F2", provider);
                var ratio = (score - 65) / 50.0f;
                if (ratio < 0) ratio = 0;
                else if (ratio > 1) ratio = 1;
                Fill = (float)Math.Pow(ratio, 0.6f);
            }
        }

        internal readonly struct GridCell {
            internal readonly ScoreText Text;
            internal readonly float Quality;

            internal GridCell(float score, float min, float max, IFormatProvider provider) {
                Text = new ScoreText(score, "F1", provider);
                var ratio = (score - min) / (max - min);
                Quality = ratio switch { <= 0 => 0, >= 1 => 1, _ => ratio };
            }
        }

        internal readonly struct DetailRow {
            internal readonly string Label;
            internal readonly string Left;
            internal readonly string Right;

            internal DetailRow(string label, float left, float right, IFormatProvider provider, bool percentage) {
                Label = label;
                Left = percentage ? (left * 100f).ToString("F2", provider) + "<size=60%>%" : left.ToString("F3", provider);
                Right = percentage ? (right * 100f).ToString("F2", provider) + "<size=60%>%" : right.ToString("F3", provider);
            }
        }

        internal IReadOnlyList<string> Averages { get; }
        internal IReadOnlyList<Hand> Hands { get; }
        internal IReadOnlyList<DetailRow> Rows { get; }
        internal IReadOnlyList<GridCell> Grid { get; }
        internal string PlatformText { get; }
        internal string DetailsText { get; }
        internal string XText { get; }
        internal string YText { get; }
        internal string ZText { get; }

        private ScoreStatsPresentation(Snapshot snapshot, CancellationToken token) {
            var provider = snapshot.NumberFormat;
            var averages = new string[snapshot.Averages.Length];
            for (var i = 0; i < averages.Length; i++) {
                token.ThrowIfCancellationRequested();
                averages[i] = snapshot.Averages[i].ToString("F2", provider);
            }
            Averages = Array.AsReadOnly(averages);
            Hands = Array.AsReadOnly(new[] { new Hand(snapshot.AccLeft, provider), new Hand(snapshot.AccRight, provider) });
            Rows = Array.AsReadOnly(new[] {
                new DetailRow("TD", snapshot.TimeLeft, snapshot.TimeRight, provider, false),
                new DetailRow("Pre", snapshot.PreLeft, snapshot.PreRight, provider, true),
                new DetailRow("Post", snapshot.PostLeft, snapshot.PostRight, provider, true)
            });

            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var score in snapshot.Grid) {
                token.ThrowIfCancellationRequested();
                if (score == 0) continue;
                if (score > max) max = score;
                if (score < min) min = score;
            }
            var cells = new GridCell[GridCellsCount];
            for (var i = 0; i < cells.Length; i++) {
                token.ThrowIfCancellationRequested();
                cells[i] = new GridCell(snapshot.Grid[i], min, max, provider);
            }
            Grid = Array.AsReadOnly(cells);

            var split = snapshot.Platform.Split(',');
            PlatformText = split.Length < 3
                ? $"<color={Faded}><bll>ls-user-platform</bll>: <color={Neutral}><bll>ls-unknown</bll> "
                : $"<color={Faded}><bll>ls-user-platform</bll>: <color={Neutral}>{FormatUtils.GetFullPlatformName(split[0])}"
                    + $"\r\n<color={Faded}><bll>ls-game</bll>: <color={Neutral}>{split[1]}    "
                    + $"<color={Faded}><bll>ls-mod</bll>: <color={Neutral}>{split[2]}    ";
            DetailsText = $"<color={Faded}>JD: <color={Neutral}>{snapshot.JumpDistance.ToString("F2", provider)}    "
                + $"<color={Faded}><bll>ls-user-height</bll>: <color={Neutral}>{snapshot.Height.ToString("F2", provider)}<size=70%>m</size>    ";
            XText = Position(snapshot.X, "X", "#FF8888", provider);
            YText = Position(snapshot.Y, "Y", "#88FF88", provider);
            ZText = Position(snapshot.Z, "Z", "#8888FF", provider);
            token.ThrowIfCancellationRequested();
        }

        private static string Position(float value, string label, string color, IFormatProvider provider) {
            return $"<color={color}><size=80%>{label}</size>  {value.ToString("F2", provider)}<size=70%>m</size>    ";
        }

        // Capture mutable API data before queuing only owned managed values.
        internal static Task<ScoreStatsPresentation> PrepareAsync(string platform, ScoreStats stats, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            var snapshot = new Snapshot(platform, stats);
            return Task.Run(() => new ScoreStatsPresentation(snapshot, token), token);
        }

        private sealed class Snapshot {
            internal readonly NumberFormatInfo NumberFormat;
            internal readonly string Platform;
            internal readonly float[] Averages;
            internal readonly float[] Grid;
            internal readonly float AccLeft, AccRight, TimeLeft, TimeRight, PreLeft, PreRight, PostLeft, PostRight;
            internal readonly float JumpDistance, Height, X, Y, Z;

            internal Snapshot(string platform, ScoreStats stats) {
                NumberFormat = NumberFormatInfo.ReadOnly((NumberFormatInfo)CultureInfo.CurrentCulture.NumberFormat.Clone());
                Platform = platform;
                var accuracy = stats.accuracyTracker;
                Averages = new[] {
                    accuracy.leftAverageCut[0], accuracy.leftAverageCut[1], accuracy.leftAverageCut[2],
                    accuracy.rightAverageCut[0], accuracy.rightAverageCut[1], accuracy.rightAverageCut[2]
                };
                Grid = (float[])accuracy.gridAcc.Clone();
                AccLeft = accuracy.accLeft;
                AccRight = accuracy.accRight;
                TimeLeft = accuracy.leftTimeDependence;
                TimeRight = accuracy.rightTimeDependence;
                PreLeft = accuracy.leftPreswing;
                PreRight = accuracy.rightPreswing;
                PostLeft = accuracy.leftPostswing;
                PostRight = accuracy.rightPostswing;
                var win = stats.winTracker;
                JumpDistance = win.jumpDistance;
                Height = win.averageHeight;
                X = win.averageHeadPosition.x;
                Y = win.averageHeadPosition.y;
                Z = win.averageHeadPosition.z;
            }
        }
    }
}
