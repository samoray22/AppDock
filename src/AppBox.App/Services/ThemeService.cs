using System.Windows;
using System.Windows.Media;
using AppBox.Core;
using AppBox.Infrastructure;
using AppBox.App.Views;
namespace AppBox.App.Services;

public static class ThemeService
{
    public static bool Dark { get; private set; }
    public static bool Windows11StyleEnabled { get; private set; } = true;
    public static IReadOnlyDictionary<string, string> Palette(bool dark)
    {
        string[] keys = ["Surface", "Card", "Ink", "Muted", "Line", "Accent", "Hover", "SelectionFill", "ToggleOff", "PreviewSurface", "SidebarSurface", "AccentInk", "AccentText"];
        string[] values = dark
            ? ["#202124", "#292B30", "#F5F5F5", "#B5BBC4", "#4B5058", "#60B5FF", "#263D56", "#223B55", "#636974", "#353A44", "#18232E", "#FFFFFF", "#8CC8FF"]
            : ["#F3F6FA", "#FFFFFF", "#1B2028", "#535F70", "#D9DFE8", "#0078D4", "#E3F0FF", "#EFF7FF", "#8B929C", "#DCE3EC", "#EAF1FA", "#FFFFFF", "#005A9E"];
        return keys.Zip(values).ToDictionary(x => x.First, x => x.Second);
    }
    public static void Apply(AppTheme theme)
    {
        Dark = theme == AppTheme.Dark || theme == AppTheme.System && SystemTheme.IsDark();
        foreach (var pair in Palette(Dark)) System.Windows.Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
        ApplyWindowStyles();
    }
    public static void ConfigureWindows11Style(bool enabled) { Windows11StyleEnabled = enabled; ApplyWindowStyles(); }
    private static void ApplyWindowStyles()
    {
        foreach (Window window in System.Windows.Application.Current.Windows)
            if (window is SettingsWindow settings) settings.ApplyWindowStyle(); else WindowPlacement.Round(window, Dark);
    }
}
