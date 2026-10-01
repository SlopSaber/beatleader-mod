using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BGLib.Polyglot;
using Newtonsoft.Json.Linq;
using TMPro;

namespace BeatLeader {
    public static class BLLocalization {
        #region Initialize

        private static LocalizationLanguage _baseGameLanguage = LocalizationLanguage.English;

        internal static void Initialize(SettingsManager settingsmanager) {
            _baseGameLanguage = Localization.Instance.supportedLanguages.FirstOrDefault(l => l.ToSerializedName() == settingsmanager.settings.misc.language);
            OnBaseGameLanguageDidChange();
            Prewarm();
        }

        #endregion

        #region Language

        private static BLLanguage _blLanguageAnalog = BLLanguage.English;

        public static BLLanguage GetCurrentLanguage() {
            return PluginConfig.SelectedLanguage == BLLanguage.GameDefault ? _blLanguageAnalog : PluginConfig.SelectedLanguage;
        }

        private static void OnBaseGameLanguageDidChange() {
            _blLanguageAnalog = _baseGameLanguage switch {
                LocalizationLanguage.English => BLLanguage.English,
                LocalizationLanguage.Russian => BLLanguage.Russian,
                LocalizationLanguage.Japanese => BLLanguage.Japanese,
                LocalizationLanguage.Simplified_Chinese => BLLanguage.Chinese,
                LocalizationLanguage.Traditional_Chinese => BLLanguage.Chinese,
                LocalizationLanguage.Korean => BLLanguage.Korean,
                LocalizationLanguage.French => BLLanguage.French,
                LocalizationLanguage.German => BLLanguage.German,
                LocalizationLanguage.Spanish => BLLanguage.Spanish,
                LocalizationLanguage.Norwegian => BLLanguage.Norwegian,
                LocalizationLanguage.Polish => BLLanguage.Polish,
                LocalizationLanguage.Swedish => BLLanguage.Swedish,
                LocalizationLanguage.Italian => BLLanguage.Italian,
                _ => BLLanguage.English,
            };
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0016", Justification = "Retain the concrete collection type in this published API for binary compatibility.")]
        public static List<BLLanguage> SupportedLanguagesSorted() {
            return new List<BLLanguage> {
                BLLanguage.GameDefault,
                BLLanguage.English,
                BLLanguage.Japanese,
                BLLanguage.Russian,
                BLLanguage.Chinese,
                BLLanguage.Korean,
                BLLanguage.French,
                BLLanguage.German,
                // BLLanguage.Spanish,
                // BLLanguage.Norwegian,
                BLLanguage.Polish,
                BLLanguage.Swedish,
                BLLanguage.Italian,
                BLLanguage.MinecraftEnchantment
            };
        }

        public static string GetLanguageName(BLLanguage language) {
            return language switch {
                BLLanguage.GameDefault => "Game Default",
                BLLanguage.English => "English",
                BLLanguage.Japanese => "Japanese",
                BLLanguage.Russian => "Russian",
                BLLanguage.Chinese => "Chinese",
                BLLanguage.Korean => "Korean",
                BLLanguage.French => "French",
                BLLanguage.German => "German",
                BLLanguage.Spanish => "Spanish",
                BLLanguage.Norwegian => "Norwegian",
                BLLanguage.Polish => "Polish",
                BLLanguage.Swedish => "Swedish",
                BLLanguage.Italian => "Italian",
                BLLanguage.MinecraftEnchantment => "Minecraft Enchantment",
                _ => "Unknown"
            };
        }

        #endregion

        #region Translations

        private const string LocalizationResourcePath = Plugin.ResourcesPath + ".Localization.json";
        private static readonly object TranslationGate = new();
        private static readonly Dictionary<BLLanguage, Task<Dictionary<string, string>>> TranslationPreparations = new();

        internal static void Prewarm() {
            _ = GetTranslationPreparation(GetCurrentLanguage());
        }

        private static Task<Dictionary<string, string>> GetTranslationPreparation(BLLanguage language) {
            lock (TranslationGate) {
                if (TranslationPreparations.TryGetValue(language, out var preparation)) {
                    if (!preparation.IsFaulted && !preparation.IsCanceled) return preparation;
                    _ = preparation.Exception;
                }

                preparation = Task.Run(() => LoadTranslations(language));
                TranslationPreparations[language] = preparation;
                return preparation;
            }
        }

        private static Dictionary<string, string> GetTranslations(BLLanguage language) {
            return GetTranslationPreparation(language).GetAwaiter().GetResult();
        }

        private static Dictionary<string, string> LoadTranslations(BLLanguage language) {
            var translations = new Dictionary<string, string>();

            if (JObject.Parse(ResourcesUtils.GetEmbeddedResourceText(LocalizationResourcePath))["Tokens"] is JArray tokensArray) {
                foreach (var tokenObject in tokensArray) {
                    var token = tokenObject.Value<string>("Token");
                    if (token == null || tokenObject["Translations"] is not JObject translationsObject) continue;
                    var translation = translationsObject[language.ToString()]?.Value<string>() ?? translationsObject["English"]?.Value<string>();
                    if (translation == null) continue;
                    translations[token] = translation;
                }
            }

            return translations;
        }

        public static bool IsValidToken(string? token) {
            var translations = GetTranslations(GetCurrentLanguage());
            return token != null && translations.ContainsKey(token);
        }

        public static string GetTranslation(string token) {
            var translations = GetTranslations(GetCurrentLanguage());
            return translations.ContainsKey(token) ? translations[token] : token;
        }

        public static string GetTranslationWithFont(string token) {
            var translations = GetTranslations(GetCurrentLanguage());
            if (!translations.ContainsKey(token)) return token;
            var fontAsset = GetLanguageFont();
            return fontAsset != null ? $"<font={fontAsset.name}>{translations[token]}</font>" : translations[token];
        }

        #endregion

        #region Fonts

        public static TMP_FontAsset? GetLanguageFont() {
            return GetCurrentLanguage() switch {
                BLLanguage.Japanese => BundleLoader.NotoSansJPFontAsset,
                BLLanguage.Russian => BundleLoader.NotoSansFontAsset,
                BLLanguage.Chinese => BundleLoader.NotoSansSCFontAsset,
                BLLanguage.Korean => BundleLoader.NotoSansKRFontAsset,
                BLLanguage.Polish => BundleLoader.NotoSansFontAsset,
                BLLanguage.MinecraftEnchantment => BundleLoader.MinecraftEnchantmentFontAsset,
                BLLanguage.English => default,
                _ => default
            };
        }

        #endregion
    }
}
