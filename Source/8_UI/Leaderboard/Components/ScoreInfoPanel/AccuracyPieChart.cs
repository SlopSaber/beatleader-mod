using System;
using BeatSaberMarkupLanguage.Attributes;
using HMUI;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;

namespace BeatLeader.Components {
    internal class AccuracyPieChart : ReeUIComponentV2 {
        #region Events

        protected override void OnInitialize() {
            InitializeBackground();
        }

        private void OnHoverStateChanged(bool isHovered, float progress) {
            UpdateVisuals(isHovered, progress);
        }

        #endregion

        #region Type

        public enum Type {
            Left,
            Right
        }

        #endregion

        #region SetValues

        private Type _type;
        private float _score;
        private ScoreStatsPresentation.ScoreText? _preparedText;

        public void SetValues(Type type, float score) {
            _type = type;
            _score = score;
            _preparedText = null;
            UpdateVisuals(_hoverController.IsHovered, _hoverController.Progress);
            SetFillValue(CalculateFillValue(score));
        }

        internal bool SetPreparedValues(Type type, ScoreStatsPresentation.Hand value, Func<bool> canApply) {
            if (!CanPublish()) return false;
            _type = type;
            _score = value.Text.Score;
            _preparedText = value.Text;
            UpdateVisuals(_hoverController.IsHovered, _hoverController.Progress);
            if (!CanPublish()) return false;
            SetFillValue(value.Fill);
            return CanPublish();

            bool CanPublish() => canApply() && this && IsHierarchySet && Content;
        }

        private void UpdateVisuals(bool isHovered, float progress) {
            _backgroundImage.color = GetColor(_type, progress);
            _textComponent.text = _preparedText is { } text
                ? (isHovered ? text.Hovered : text.Plain)
                : FormatScore(_score, isHovered);
        }

        #endregion

        #region Formatting

        private static readonly Color LeftColor = new(0.8f, 0.2f, 0.2f, 0.1f);
        private static readonly Color RightColor = new(0.2f, 0.2f, 0.8f, 0.1f);
        private const float HoveredGlow = 0.5f;

        private static Color GetColor(Type type, float hover) {
            var col = type switch {
                Type.Left => LeftColor,
                Type.Right => RightColor,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
            col.a = Mathf.Lerp(col.a, HoveredGlow, hover);
            return col;
        }

        private static float CalculateFillValue(float score) {
            var ratio = Mathf.Clamp01((score - 65) / 50.0f);
            return Mathf.Pow(ratio, 0.6f);
        }

        private static string FormatScore(float value, bool showAcc) {
            if (!showAcc) return $"{value:F2}";
            var acc = value / 1.15f;
            return $"<line-height=53%>{value:F2}\n<size=80%>{acc:F2}<size=50%>%";
        }

        #endregion

        #region Text

        [UIComponent("text-component"), UsedImplicitly]
        private TextMeshProUGUI _textComponent = default!;

        #endregion

        #region Background

        [UIComponent("background"), UsedImplicitly]
        private ImageView _backgroundImage = default!;

        private SmoothHoverController _hoverController;

        private static readonly int FillPropertyId = Shader.PropertyToID("_FillValue");
        private Material _materialInstance;

        private void SetFillValue(float value) {
            _materialInstance.SetFloat(FillPropertyId, value);
        }

        private void InitializeBackground() {
            _materialInstance = Material.Instantiate(BundleLoader.HandAccIndicatorMaterial);
            _backgroundImage.material = _materialInstance;
            _backgroundImage.raycastTarget = true;
            _hoverController = SmoothHoverController.Custom(_backgroundImage.gameObject, OnHoverStateChanged);
        }

        #endregion
    }
}