using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Runtime.Serialization;

namespace BeatLeader.Models {
    [PublicAPI]
    public class ReplayerSettings {
        public static ReplayerSettings DefaultSettings => ConfigDefaults.ReplayerSettings;
        public static ReplayerSettings UserSettings => ConfigFileData.Instance.ReplayerSettings;

        public bool ExitReplayAutomatically { get; set; }
        public bool LoadPlayerEnvironment { get; set; }
        public bool LoadPlayerJumpDistance { get; set; }
        public bool IgnoreModifiers { get; set; }

        public bool ShowWatermark { get; set; }

        public ReplayerShortcuts Shortcuts { get; set; } = new();
        public ReplayerUISettings UISettings { get; set; } = new();
        public BodySettings BodySettings { get; set; } = new();

        private bool? _legacyShowHead;
        private bool? _legacyShowLeftSaber;
        private bool? _legacyShowRightSaber;

        [JsonProperty("ShowHead")]
        private bool? LegacyShowHead { set => _legacyShowHead = value; }
        [JsonProperty("ShowLeftSaber")]
        private bool? LegacyShowLeftSaber { set => _legacyShowLeftSaber = value; }
        [JsonProperty("ShowRightSaber")]
        private bool? LegacyShowRightSaber { set => _legacyShowRightSaber = value; }

        [OnDeserialized]
        private void RestoreBodySettings(StreamingContext context) {
            BodySettings ??= new BodySettings();
            bool missingPrimary = !BodySettings.Configs.ContainsKey(typeof(BasicBodySettings));
            foreach (var (type, config) in DefaultSettings.BodySettings.Configs) {
                if (!BodySettings.Configs.ContainsKey(type)) {
                    BodySettings.Configs[type] = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(config), type)!;
                }
            }

            if (!missingPrimary) return;
            var primary = BodySettings.RequireConfig<BasicBodySettings>();
            primary.HeadEnabled = _legacyShowHead ?? primary.HeadEnabled;
            primary.LeftSaberEnabled = _legacyShowLeftSaber ?? primary.LeftSaberEnabled;
            primary.RightSaberEnabled = _legacyShowRightSaber ?? primary.RightSaberEnabled;
        }
        
        // This property exists as a fix for previous versions that were broken because of the camera settings being null
        public ReplayerCameraSettings CameraSettings {
            get => field ??= ConfigDefaults.ReplayerSettings.CameraSettings;
            set => field = value ?? ConfigDefaults.ReplayerSettings.CameraSettings;
        }
    }
}
