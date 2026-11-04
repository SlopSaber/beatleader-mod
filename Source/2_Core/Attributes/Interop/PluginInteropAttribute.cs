using System;
using JetBrains.Annotations;

namespace BeatLeader.Attributes {
    [AttributeUsage(AttributeTargets.Class), MeansImplicitUse]
    internal class PluginInteropAttribute : Attribute {
        public PluginInteropAttribute(string pluginId, string? worksUntilVersion = null, string? supersededBy = null) {
            this.pluginId = pluginId;
            this.worksUntilVersion = worksUntilVersion;
            this.supersededBy = supersededBy;
        }

        public readonly string pluginId;
        public readonly string? worksUntilVersion;
        public readonly string? supersededBy;
    }
}
