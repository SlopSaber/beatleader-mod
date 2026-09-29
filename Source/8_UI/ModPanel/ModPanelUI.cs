using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Util;
using JetBrains.Annotations;

namespace BeatLeader {
    internal partial class ModPanelUI : NotifiableSingleton<ModPanelUI> {
        #region Template button

        [UIValue("template-button-text")] [UsedImplicitly]
        private string TemplateButtonText => "Hello world!";

        #endregion
    }
}