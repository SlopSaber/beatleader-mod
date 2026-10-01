using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using BeatLeader.Models;
using Reactive;
using Reactive.BeatSaber.Components;
using Reactive.Components;
using Reactive.Yoga;
using TMPro;
using UnityEngine;

namespace BeatLeader.UI.Hub {
    internal class BeatmapPreviewPanel : ReactiveComponent, ISkewedComponent {
        #region Skew

        public float Skew {
            get => _skew;
            set {
                _skew = value;
                var style = value > 0 ? FontStyles.Italic : FontStyles.Normal;
                _songNameLabel.FontStyle = style;
                _songAuthorLabel.FontStyle = style;
                _songTimeLabel.FontStyle = style;
                _songBpmLabel.FontStyle = style;
                _songDifficultyLabel.FontStyle = style;
                _songImage.Skew = value;
                _songDifficultyImage.Skew = value;
            }
        }

        private float _skew;

        #endregion

        #region Setup

        public bool ShowDifficultyInsteadOfTime {
            set {
                _songDifficultyContainer.Enabled = value;
                _songTimeLabel.Enabled = !value;
                _songBpmLabel.Enabled = !value;
            }
        }

        private CancellationTokenSource? _previewCancellation;
        private long _previewRevision;
        private bool _destroyed;

        public async Task SetBeatmap(BeatmapLevelWithKey beatmap) {
            await SetPreviewAsync(beatmap.Level, beatmap.Key.difficulty, beatmap.Key.characteristic.SerializedName());
        }

        public Task SetBeatmapLevel(BeatmapLevel level) {
            return SetPreviewAsync(level, null, null);
        }

        private bool IsCurrent(CancellationTokenSource source, long revision) {
            return !_destroyed && ReferenceEquals(_previewCancellation, source) && _previewRevision == revision &&
                !source.IsCancellationRequested && IsInitialized && !IsDestroyed && Content;
        }

        private async Task SetPreviewAsync(BeatmapLevel level, BeatmapDifficulty? difficulty, string? characteristic) {
            if (_destroyed || IsDestroyed) return;
            var previous = _previewCancellation;
            var source = new CancellationTokenSource();
            _previewCancellation = source;
            var revision = ++_previewRevision;
            previous?.Cancel();
            var token = source.Token;
            try {
                var name = level.songName;
                var subName = level.songSubName;
                var author = level.songAuthorName;
                var mappers = level.allMappers.ToArray();
                var duration = level.songDuration;
                var bpm = level.beatsPerMinute;
                var media = level.previewMediaData;
                var format = NumberFormatInfo.ReadOnly((NumberFormatInfo)CultureInfo.CurrentCulture.NumberFormat.Clone());
                var text = await Task.Run(() => (
                    name: FormatSongNameText(name, subName),
                    author: FormatAuthorText(author, string.Join(", ", mappers)),
                    duration: FormatUtils.FormatTime(Mathf.FloorToInt(duration), format),
                    bpm: Mathf.FloorToInt(bpm).ToString(format),
                    difficulty: difficulty?.ToString()
                ), token);
                if (!IsCurrent(source, revision)) return;
                _songNameLabel.Text = text.name;
                if (!IsCurrent(source, revision)) return;
                _songAuthorLabel.Text = text.author;
                if (!IsCurrent(source, revision)) return;
                _songTimeLabel.Text = text.duration;
                if (!IsCurrent(source, revision)) return;
                _songBpmLabel.Text = text.bpm;
                if (!IsCurrent(source, revision)) return;
                var sprite = await media.GetCoverSpriteAsync();
                if (!IsCurrent(source, revision)) return;
                _songImage.Sprite = sprite;
                if (difficulty.HasValue && IsCurrent(source, revision)) {
                    _songDifficultyLabel.Text = text.difficulty!;
                    if (!IsCurrent(source, revision)) return;
                    var icon = Resources.FindObjectsOfTypeAll<BeatmapCharacteristicSO>()
                        .FirstOrDefault(x => x.serializedName == characteristic)?.icon;
                    if (IsCurrent(source, revision)) _songDifficultyImage.Sprite = icon;
                }
            } catch (System.OperationCanceledException) when (token.IsCancellationRequested) {
            } finally {
                if (ReferenceEquals(_previewCancellation, source)) _previewCancellation = null;
                source.Dispose();
            }
        }

        protected override void OnDestroy() {
            _destroyed = true;
            _previewRevision++;
            var source = _previewCancellation;
            _previewCancellation = null;
            source?.Cancel();
            base.OnDestroy();
        }

        private static string FormatSongNameText(string name, string subName) {
            return $"{name} <size=80%>{subName}</size>";
        }

        private static string FormatAuthorText(string author, string mapper) {
            var text = $"<size=80%>{author}</size>";
            
            if (!string.IsNullOrEmpty(mapper)) {
                text += $" <size=90%>[<color=#89ff89>{mapper}</color>]</size>";
            }
            
            return text;
        }

        #endregion

        #region Construct

        private Layout _songDifficultyContainer = null!;
        private Label _songDifficultyLabel = null!;
        private Image _songDifficultyImage = null!;

        private Label _songNameLabel = null!;
        private Label _songAuthorLabel = null!;
        private Label _songTimeLabel = null!;
        private Label _songBpmLabel = null!;

        private Layout _background = null!;
        private Image _songImage = null!;

        protected override GameObject Construct() {
            static Label Label(
                TextOverflowModes overflow,
                TextAlignmentOptions alignment,
                float minFontSize,
                float maxFontSize,
                float size,
                Color color,
                ref Label variable
            ) {
                return new Label {
                    Overflow = overflow,
                    Alignment = alignment,
                    FontStyle = FontStyles.Italic,
                    FontSizeMin = minFontSize,
                    FontSizeMax = maxFontSize,
                    EnableAutoSizing = true,
                    Color = color
                }.AsFlexItem(flexGrow: size).Bind(ref variable);
            }

            var primaryColor = Color.white;
            var secondaryColor = Color.white.ColorWithAlpha(0.75f);

            return new Layout {
                Children = {
                    new Image {
                        Sprite = BundleLoader.UnknownIcon,
                        Material = GameResources.UINoGlowRoundEdgeMaterial
                    }.AsFlexItem(aspectRatio: 1f).Bind(ref _songImage),
                    //
                    new Layout {
                        Children = {
                            //top rail
                            new Layout {
                                Children = {
                                    //song name
                                    Label(
                                        TextOverflowModes.Ellipsis,
                                        TextAlignmentOptions.BottomLeft,
                                        4f,
                                        5f,
                                        8f,
                                        primaryColor,
                                        ref _songNameLabel
                                    ),
                                    //song time
                                    Label(
                                        TextOverflowModes.Overflow,
                                        TextAlignmentOptions.BottomRight,
                                        4f,
                                        5f,
                                        2f,
                                        primaryColor,
                                        ref _songTimeLabel
                                    )
                                }
                            }.AsFlexGroup().AsFlexItem(
                                position: new() { top = 0f },
                                size: new() { x = 100.pct(), y = 70.pct() },
                                margin: new() { left = 0.7f, right = 1.5f }
                            ),
                            //bottom rail
                            new Layout {
                                Children = {
                                    //song author
                                    Label(
                                        TextOverflowModes.Ellipsis,
                                        TextAlignmentOptions.TopLeft,
                                        3f,
                                        4f,
                                        7f,
                                        secondaryColor,
                                        ref _songAuthorLabel
                                    ),
                                    //song bpm
                                    Label(
                                        TextOverflowModes.Overflow,
                                        TextAlignmentOptions.TopRight,
                                        3f,
                                        4f,
                                        3f,
                                        secondaryColor,
                                        ref _songBpmLabel
                                    )
                                }
                            }.AsFlexGroup().AsFlexItem(
                                position: new() { bottom = 0f },
                                size: new() { x = 100.pct(), y = 50.pct() },
                                margin: new() { right = 2.5f }
                            ),
                            //difficulty
                            new Layout {
                                Children = {
                                    new Label {
                                        FontSize = 4f,
                                        Color = secondaryColor
                                    }.AsFlexItem(size: "auto").Bind(ref _songDifficultyLabel),
                                    //
                                    new Image {
                                        Sprite = BundleLoader.Sprites.transparentPixel,
                                        Color = secondaryColor,
                                        PreserveAspect = true
                                    }.AsFlexItem(size: 4f).Bind(ref _songDifficultyImage)
                                }
                            }.AsFlexGroup(
                                alignItems: Align.Center,
                                gap: 1f
                            ).AsFlexItem(
                                size: new() { y = "100%" },
                                position: new() { right = 0f }
                            ).Bind(ref _songDifficultyContainer)
                            //
                        }
                    }.AsFlexGroup(
                        direction: FlexDirection.Column,
                        justifyContent: Justify.Center
                    ).AsFlexItem(
                        flexGrow: 1f,
                        minSize: new() { y = 8f },
                        maxSize: new() { y = "100%" },
                        size: new() { y = 12f },
                        margin: new() { right = 2f },
                        alignSelf: Align.Center
                    )
                }
            }.AsFlexGroup(gap: 0.8f).Bind(ref _background).Use();
        }

        protected override void OnInitialize() {
            this.AsFlexItem(size: new() { x = 50f, y = 10f });
            ShowDifficultyInsteadOfTime = false;
        }

        #endregion
    }
}
