using System.Windows;
using AppBox.Core;

namespace AppBox.App.Services;

public static class LanguageResourceService
{
    public static void Apply(AppLanguage language)
    {
        LocalizationService.SetLanguage(language);
        UpdateResources();
    }

    public static void UpdateResources()
    {
        if (Application.Current is null) return;
        var dict = LocalizationService.GetDictionary(LocalizationService.EffectiveCultureCode);
        foreach (var key in LocalizationService.GetAllBaselineKeys())
        {
            var value = dict.TryGetValue(key, out var localized) ? localized : LocalizationService.Get(key);
            Application.Current.Resources["Str." + key] = value;
        }
    }
}
