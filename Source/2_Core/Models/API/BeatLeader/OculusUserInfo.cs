namespace BeatLeader.Models {
    internal struct OculusUserInfo {
        public string id { get; set; }
        public string name { get; set; }
        public string avatar { get; set; }
        public bool migrated { get; set; }
        public string migratedId { get; set; }
    }
}