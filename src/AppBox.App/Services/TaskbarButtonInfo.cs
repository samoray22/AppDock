using System.Windows.Media;
using AppBox.Core;

namespace AppBox.App.Services;

public sealed record TaskbarButtonInfo(
    PixelRect Bounds,
    string Name,
    string CleanName,
    string AutomationId,
    bool IsAppDock,
    Guid? ContainerId,
    ImageSource? Icon);
