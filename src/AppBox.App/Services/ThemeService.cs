using System.Windows;
using System.Windows.Media;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.Services;

public static class ThemeService
{
    public static bool Dark { get; private set; }
    public static void Apply(AppTheme theme)
    {
        Dark = theme == AppTheme.Dark || theme == AppTheme.System && SystemTheme.IsDark();
        var colors = Dark
            ? new[] { "#20212B", "#2A2C39", "#F2F1FA", "#ACB0C5", "#3C3E50", "#A49AFF", "#3D385C" }
            : new[] { "#F6F7FB", "#FFFFFF", "#202436", "#666D80", "#E1E4EE", "#635BDB", "#EBE9FC" };
        var keys = new[] { "Surface", "Card", "Ink", "Muted", "Line", "Accent", "Hover" };
        for (var i = 0; i < keys.Length; i++)
            System.Windows.Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        foreach (Window window in System.Windows.Application.Current.Windows) WindowPlacement.Round(window, Dark);
    }
}
