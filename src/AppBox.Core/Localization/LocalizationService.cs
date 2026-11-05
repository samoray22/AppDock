using System.Globalization;

namespace AppBox.Core;

public static class LocalizationService
{
    private static AppLanguage currentLanguage = AppLanguage.System;
    private static string effectiveCultureCode = "en";
    private static IReadOnlyDictionary<string, string> activeStrings = Strings_en.Map;

    public static AppLanguage CurrentLanguage => currentLanguage;
    public static string EffectiveCultureCode => effectiveCultureCode;

    public static event Action? LanguageChanged;

    public static readonly (AppLanguage Language, string Code, string DisplayName)[] SupportedLanguages =
    [
        (AppLanguage.System, "system", "System Default (Auto)"),
        (AppLanguage.English, "en", "English"),
        (AppLanguage.Spanish, "es", "Español (Spanish)"),
        (AppLanguage.French, "fr", "Français (French)"),
        (AppLanguage.German, "de", "Deutsch (German)"),
        (AppLanguage.Italian, "it", "Italiano (Italian)"),
        (AppLanguage.Portuguese, "pt", "Português (Portuguese)"),
        (AppLanguage.Dutch, "nl", "Nederlands (Dutch)"),
        (AppLanguage.Turkish, "tr", "Türkçe (Turkish)"),
        (AppLanguage.Russian, "ru", "Русский (Russian)"),
        (AppLanguage.Chinese, "zh", "中文 (简体) (Chinese Simplified)"),
        (AppLanguage.TraditionalChinese, "zh-Hant", "中文 (繁體) (Chinese Traditional)"),
        (AppLanguage.Japanese, "ja", "日本語 (Japanese)"),
        (AppLanguage.Korean, "ko", "한국어 (Korean)")
    ];

    static LocalizationService()
    {
        SetLanguage(AppLanguage.System);
    }

    public static void SetLanguage(AppLanguage language)
    {
        currentLanguage = language;
        effectiveCultureCode = ResolveCultureCode(language);
        activeStrings = GetDictionary(effectiveCultureCode);
        LanguageChanged?.Invoke();
    }

    public static string ResolveCultureCode(AppLanguage language)
    {
        if (language != AppLanguage.System)
        {
            return language switch
            {
                AppLanguage.English => "en",
                AppLanguage.Spanish => "es",
                AppLanguage.French => "fr",
                AppLanguage.German => "de",
                AppLanguage.Italian => "it",
                AppLanguage.Portuguese => "pt",
                AppLanguage.Dutch => "nl",
                AppLanguage.Turkish => "tr",
                AppLanguage.Russian => "ru",
                AppLanguage.Chinese => "zh",
                AppLanguage.TraditionalChinese => "zh-Hant",
                AppLanguage.Japanese => "ja",
                AppLanguage.Korean => "ko",
                _ => "en"
            };
        }

        var uiCulture = CultureInfo.CurrentUICulture;
        var culture = uiCulture.TwoLetterISOLanguageName.ToLowerInvariant();
        if (culture == "zh")
        {
            var name = uiCulture.Name;
            if (name.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
                uiCulture.ThreeLetterWindowsLanguageName.Equals("CHT", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-Hant";
            }
            return "zh";
        }

        return culture switch
        {
            "es" => "es",
            "fr" => "fr",
            "de" => "de",
            "it" => "it",
            "pt" => "pt",
            "nl" => "nl",
            "tr" => "tr",
            "ru" => "ru",
            "ja" => "ja",
            "ko" => "ko",
            _ => "en"
        };
    }

    public static IReadOnlyDictionary<string, string> GetDictionary(string cultureCode) => cultureCode?.ToLowerInvariant() switch
    {
        "es" => Strings_es.Map,
        "fr" => Strings_fr.Map,
        "de" => Strings_de.Map,
        "it" => Strings_it.Map,
        "pt" => Strings_pt.Map,
        "nl" => Strings_nl.Map,
        "tr" => Strings_tr.Map,
        "ru" => Strings_ru.Map,
        "zh" => Strings_zh.Map,
        "zh-hant" or "zh-tw" or "zh-hk" or "zh-mo" or "zh-cht" => Strings_zh_Hant.Map,
        "ja" => Strings_ja.Map,
        "ko" => Strings_ko.Map,
        _ => Strings_en.Map
    };

    public static string Get(string key)
    {
        if (activeStrings.TryGetValue(key, out var val)) return val;
        if (Strings_en.Map.TryGetValue(key, out var fallback)) return fallback;
        return key;
    }

    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch
        {
            return template;
        }
    }

    public static string GetCategoryDisplayName(string canonicalCategory)
    {
        if (string.IsNullOrEmpty(canonicalCategory)) return "";
        var key = "Category." + canonicalCategory.Replace(" ", "").Replace("&", "And");
        if (activeStrings.TryGetValue(key, out var localized)) return localized;
        if (Strings_en.Map.TryGetValue(key, out var fallback)) return fallback;
        return canonicalCategory;
    }

    public static IEnumerable<string> GetAllBaselineKeys() => Strings_en.Map.Keys;
}
