using BeatLeader.API;
using BeatLeader.Models;
using BeatLeader.Utils;
using BeatSaberMarkupLanguage.Attributes;
using JetBrains.Annotations;
using System;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatLeader.Components {
    internal class PrestigePanel : AbstractReeModal<object> {
        #region Init / Dispose

        private FireworksController? fireworksController = null;

        [UIComponent("primaryText"), UsedImplicitly]
        private TextMeshProUGUI primaryText = default!;

        [UIComponent("secondaryText"), UsedImplicitly]
        private TextMeshProUGUI secondaryText = default!;

        protected override void OnInitialize() {
            base.OnInitialize();
            InitializePrestigeButtons();
            UserRequest.Request.StateChangedEvent += OnProfileRequestStateChanged;
            UploadReplayRequest.Request.StateChangedEvent += OnUploadStateChanged;

            fireworksController = UnityEngine.Object.FindObjectsByType<FireworksController>(FindObjectsSortMode.None).FirstOrDefault();
        }

        protected override void OnDispose() {
            UserRequest.Request.StateChangedEvent -= OnProfileRequestStateChanged;
            UploadReplayRequest.Request.StateChangedEvent -= OnUploadStateChanged;
        }

        #endregion

        #region Events

        private void OnProfileRequestStateChanged(WebRequests.IWebRequest<Player> instance, WebRequests.RequestState state, string? failReason) {
            switch (state) {
                case WebRequests.RequestState.Finished when instance.Result is { } player:
                    UpdatePanelContent(player);
                    break;
                default: return;
            }
        }

        private void OnUploadStateChanged(WebRequests.IWebRequest<ScoreUploadResponse> instance, WebRequests.RequestState state,
            string? failReason) {
            switch (state) {
                case WebRequests.RequestState.Finished:
                    if (instance.Result is { } result && result.Status != ScoreUploadStatus.Error) {
                        UpdatePanelContent(result.Score.Player);
                    }
                    break;
                default: return;
            }
        }

        private void UpdatePanelContent(Player player) {
            bool canPrestige = player.level == 100;

            primaryText.SetText(
                    canPrestige
                        ? "<b><color=#ffffff>Congratulations!</color></b>\nYou've reached <b>level 100</b> and can now <b>Prestige</b>."
                        : "Gain experience points by playing any maps, even for failing! Reach <b>level 100</b> to be able to <b>prestige</b> into the next iteration.");

            secondaryText.SetText(
                    canPrestige
                        ? $"This will reset your level and you will reach <b>Prestige {player.prestige + 1}</b>. <color=#ffffff>Are you ready?</color>"
                        : "To get more points, pass maps always with 95+% accuracy. But even playing with 90% accuracy will give you almost the full xp for the time played.\n<color=#ffffff>Just play more!</color>");

            _PrestigeYesButton.gameObject.SetActive(canPrestige);
            _PrestigeYesButton.interactable = canPrestige;

            _PrestigeNoButton.GetComponentInChildren<TextMeshProUGUI>().SetText(canPrestige ? "No" : "Close");
        }

        public static event Action PrestigeWasPressedEvent;

        #endregion

        #region Prestige

        private void RequestPrestige() {
            PrestigeRequest.Send();
            PrestigeWasPressedEvent?.Invoke();
            _PrestigeYesButton.interactable = false;
            if (fireworksController != null) {
                _ = Fireworks(fireworksController, 5).RunCatching();
            }
            Close();
        }

        private static async Task Fireworks(FireworksController controller, double duration) {
            controller.enabled = true;
            await Task.Delay(TimeSpan.FromSeconds(duration));
            if (controller != null) controller.enabled = false;
        }

        #endregion

        #region PlaylistButtons

        [UIComponent("prestige-yes-button"), UsedImplicitly]
        private Button _PrestigeYesButton = default!;

        [UIComponent("prestige-no-button"), UsedImplicitly]
        private Button _PrestigeNoButton = default!;

        private void InitializePrestigeButtons() {
            _PrestigeYesButton.onClick.AddListener(() => RequestPrestige());
            _PrestigeYesButton.interactable = false;
            _PrestigeNoButton.onClick.AddListener(() => Close());
        }

        #endregion
    }
}
