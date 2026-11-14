using System.Collections.ObjectModel;
using AppBox.App.Services;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    public Configuration Config { get; }
    private readonly Action<bool> writeStartup;
    private readonly Func<bool> readStartup;
    private string preferenceError = "";
    public string PreferenceError { get => preferenceError; private set { preferenceError = value; Changed(); } }
    public bool Windows11StyleAvailable { get; }
    public bool IsSingleLayout { get => Config.Settings.LayoutMode == LayoutMode.SingleAppBox; set { if (value) SetPreference(Config.Settings.LayoutMode, LayoutMode.SingleAppBox, v => Config.Settings.LayoutMode = v); } }
    public bool IsMultipleLayout { get => Config.Settings.LayoutMode == LayoutMode.MultipleAppBoxes; set { if (value) SetPreference(Config.Settings.LayoutMode, LayoutMode.MultipleAppBoxes, v => Config.Settings.LayoutMode = v); } }
    public ObservableCollection<LanguageOption> AvailableLanguages { get; } = [];
    public LanguageOption? SelectedLanguageOption
    {
        get => AvailableLanguages.FirstOrDefault(l => l.Language == Config.Settings.Language) ?? AvailableLanguages.FirstOrDefault();
        set
        {
            if (value is not null && value.Language != Config.Settings.Language)
            {
                Language = value.Language;
            }
        }
    }
    public AppLanguage Language
    {
        get => Config.Settings.Language;
        set
        {
            if (Config.Settings.Language == value) return;
            SetPreference(Config.Settings.Language, value, v => Config.Settings.Language = v);
            LanguageResourceService.Apply(value);
        }
    }
    public bool ShowAppNames { get => Config.Settings.ShowApplicationNames; set => SetPreference(ShowAppNames, value, v => Config.Settings.ShowApplicationNames = v); }

    public bool ShortenAppNames { get => Config.Settings.ShortenAppNames; set => SetPreference(ShortenAppNames, value, v => Config.Settings.ShortenAppNames = v); }

    public SortMode SortMode { get => Config.Settings.SortMode; set => SetPreference(SortMode, value, v => Config.Settings.SortMode = v); }

    public bool IsManualSort { get => Config.Settings.SortMode == SortMode.Manual; set { if (value) SortMode = SortMode.Manual; } }

    public bool IsRecentFirstSort { get => Config.Settings.SortMode == SortMode.RecentFirst; set { if (value) SortMode = SortMode.RecentFirst; } }
    public int GridColumns { get => Config.Settings.GridColumns; set => SetPreference(Config.Settings.GridColumns, Math.Clamp(value, 2, 8), v => Config.Settings.GridColumns = v); }
    public int GridRows { get => Config.Settings.GridRows; set => SetPreference(Config.Settings.GridRows, Math.Clamp(value, 1, 8), v => Config.Settings.GridRows = v); }
    public bool AutoCloseAfterLaunch { get => Config.Settings.CloseAfterLaunch; set => SetPreference(AutoCloseAfterLaunch, value, v => Config.Settings.CloseAfterLaunch = v); }
    public bool EnableAnimations { get => Config.Settings.EnableAnimations; set => SetPreference(Config.Settings.EnableAnimations, value, v => Config.Settings.EnableAnimations = v); }
    public ContainerAnimationStyle AnimationStyle { get => Config.Settings.AnimationStyle; set => SetPreference(Config.Settings.AnimationStyle, value, v => Config.Settings.AnimationStyle = v); }
    public int AnimationDurationMs
    {
        get => Config.Settings.AnimationDurationMs is >= 50 and <= 2000 ? Config.Settings.AnimationDurationMs : 320;
        set => SetPreference(Config.Settings.AnimationDurationMs, Math.Clamp(value, 50, 2000), v => Config.Settings.AnimationDurationMs = v);
    }
    public bool IsGenieAnimation { get => Config.Settings.AnimationStyle == ContainerAnimationStyle.Genie; set { if (value) AnimationStyle = ContainerAnimationStyle.Genie; } }
    public bool IsSuckAnimation { get => Config.Settings.AnimationStyle == ContainerAnimationStyle.Suck; set { if (value) AnimationStyle = ContainerAnimationStyle.Suck; } }
    public bool IsScaleAnimation { get => Config.Settings.AnimationStyle == ContainerAnimationStyle.Scale; set { if (value) AnimationStyle = ContainerAnimationStyle.Scale; } }
    public bool IsMagicLampAnimation { get => Config.Settings.AnimationStyle == ContainerAnimationStyle.MagicLamp; set { if (value) AnimationStyle = ContainerAnimationStyle.MagicLamp; } }
    public bool IsOrigamiAnimation { get => Config.Settings.AnimationStyle == ContainerAnimationStyle.Origami; set { if (value) AnimationStyle = ContainerAnimationStyle.Origami; } }
    public bool IsPageCurlAnimation { get => Config.Settings.AnimationStyle == ContainerAnimationStyle.PageCurl; set { if (value) AnimationStyle = ContainerAnimationStyle.PageCurl; } }
    public bool IsBlackHoleAnimation { get => Config.Settings.AnimationStyle == ContainerAnimationStyle.BlackHole; set { if (value) AnimationStyle = ContainerAnimationStyle.BlackHole; } }
    public bool IsFlip3DAnimation { get => Config.Settings.AnimationStyle == ContainerAnimationStyle.Flip3D; set { if (value) AnimationStyle = ContainerAnimationStyle.Flip3D; } }
    public bool IsJellyAnimation { get => Config.Settings.AnimationStyle == ContainerAnimationStyle.Jelly; set { if (value) AnimationStyle = ContainerAnimationStyle.Jelly; } }
    public bool IsRandomAnimation { get => Config.Settings.AnimationStyle == ContainerAnimationStyle.Random; set { if (value) AnimationStyle = ContainerAnimationStyle.Random; } }
    public bool EnableDockMagnification { get => Config.Settings.EnableDockMagnification; set => SetPreference(Config.Settings.EnableDockMagnification, value, v => Config.Settings.EnableDockMagnification = v); }
    public bool EnableLaunchBounce { get => Config.Settings.EnableLaunchBounce; set => SetPreference(Config.Settings.EnableLaunchBounce, value, v => Config.Settings.EnableLaunchBounce = v); }
    public bool EnableCardTiltOnHover { get => Config.Settings.EnableCardTiltOnHover; set => SetPreference(Config.Settings.EnableCardTiltOnHover, value, v => Config.Settings.EnableCardTiltOnHover = v); }
    public bool EnablePoofOnRemove { get => Config.Settings.EnablePoofOnRemove; set => SetPreference(Config.Settings.EnablePoofOnRemove, value, v => Config.Settings.EnablePoofOnRemove = v); }
    public string AnimationStyleTitle => ContainerWarpMesh.GetTitle(Config.Settings.AnimationStyle);
    public string AnimationStyleTagline => ContainerWarpMesh.GetTagline(Config.Settings.AnimationStyle);
    public string AnimationStyleDescription => ContainerWarpMesh.GetDescription(Config.Settings.AnimationStyle);
    public bool UseWindows11Style { get => Config.Settings.UseWindows11Style; set { if (Windows11StyleAvailable) SetPreference(UseWindows11Style, value, v => Config.Settings.UseWindows11Style = v); } }
    public bool ShowContainerBorder { get => Config.Settings.ShowContainerBorder; set => SetPreference(ShowContainerBorder, value, v => Config.Settings.ShowContainerBorder = v); }
    public bool AllowCommandLineShells { get => Config.Settings.AllowCommandLineShells; set => SetPreference(AllowCommandLineShells, value, v => Config.Settings.AllowCommandLineShells = v); }
    public bool EnableTaskbar
    {
        get => Config.Settings.EnableTaskbar;
        set => SetActivationSurfaces(value, (!value && !Config.Settings.EnableEdgeSidebar && !Config.Settings.EnableDesktopContainers) ? true : Config.Settings.EnableEdgeSidebar, Config.Settings.EnableDesktopContainers);
    }
    public bool EnableEdgeSidebar
    {
        get => Config.Settings.EnableEdgeSidebar;
        set => SetActivationSurfaces((!value && !Config.Settings.EnableTaskbar && !Config.Settings.EnableDesktopContainers) ? true : Config.Settings.EnableTaskbar, value, Config.Settings.EnableDesktopContainers);
    }
    public bool EnableDesktopContainers
    {
        get => Config.Settings.EnableDesktopContainers;
        set => SetActivationSurfaces((!value && !Config.Settings.EnableTaskbar && !Config.Settings.EnableEdgeSidebar) ? true : Config.Settings.EnableTaskbar, Config.Settings.EnableEdgeSidebar, value);
    }
    public bool HideDesktopIcons
    {
        get => Config.Settings.HideDesktopIcons;
        set
        {
            if (Config.Settings.HideDesktopIcons == value) return;
            DesktopIconsService.SetDesktopIconsHidden(value, explicitUserAction: true);
            SetPreference(Config.Settings.HideDesktopIcons, value, v => Config.Settings.HideDesktopIcons = v);
        }
    }
    public bool IsFencesDetected { get; }
    public bool RestoreDesktopIconsOnExit
    {
        get => Config.Settings.RestoreDesktopIconsOnExit;
        set => SetPreference(Config.Settings.RestoreDesktopIconsOnExit, value, v => Config.Settings.RestoreDesktopIconsOnExit = v);
    }
    public DesktopDisplayStyle DesktopDisplayStyle
    {
        get => Config.Settings.DesktopDisplayStyle;
        set => SetPreference(Config.Settings.DesktopDisplayStyle, value, v => Config.Settings.DesktopDisplayStyle = v);
    }
    public bool IsDesktopCompactFlyout
    {
        get => Config.Settings.DesktopDisplayStyle == DesktopDisplayStyle.CompactFlyout;
        set { if (value) DesktopDisplayStyle = DesktopDisplayStyle.CompactFlyout; }
    }
    public bool IsDesktopExpandedBoxes
    {
        get => Config.Settings.DesktopDisplayStyle == DesktopDisplayStyle.ExpandedBoxes;
        set { if (value) DesktopDisplayStyle = DesktopDisplayStyle.ExpandedBoxes; }
    }
    public DesktopContentSource DesktopContentSource
    {
        get => Config.Settings.DesktopContentSource;
        set
        {
            if (Config.Settings.DesktopContentSource == value) return;
            if (value == DesktopContentSource.SeparateDesktopContainers && Config.DesktopFolders.Count == 0)
            {
                desktopManager.Create(LocalizationService.Get("Desktop.DefaultContainerName"));
            }
            SetPreference(Config.Settings.DesktopContentSource, value, v => Config.Settings.DesktopContentSource = v);
            RefreshDesktop();
        }
    }
    public bool IsDesktopSharedContent
    {
        get => Config.Settings.DesktopContentSource == DesktopContentSource.SharedWithSidebar;
        set { if (value) DesktopContentSource = DesktopContentSource.SharedWithSidebar; }
    }
    public bool IsDesktopSeparateContent
    {
        get => Config.Settings.DesktopContentSource == DesktopContentSource.SeparateDesktopContainers;
        set { if (value) DesktopContentSource = DesktopContentSource.SeparateDesktopContainers; }
    }
    public ContainerOpeningBehavior DesktopOpeningBehavior
    {
        get => Config.Settings.DesktopOpeningBehavior;
        set => SetPreference(Config.Settings.DesktopOpeningBehavior, value, v => Config.Settings.DesktopOpeningBehavior = v);
    }
    public bool IsDesktopClickOpen { get => Config.Settings.DesktopOpeningBehavior == ContainerOpeningBehavior.Click; set { if (value) DesktopOpeningBehavior = ContainerOpeningBehavior.Click; } }
    public bool IsDesktopHoverOpen { get => Config.Settings.DesktopOpeningBehavior == ContainerOpeningBehavior.Hover; set { if (value) DesktopOpeningBehavior = ContainerOpeningBehavior.Hover; } }
    public bool IsDesktopClickOrHoverOpen { get => Config.Settings.DesktopOpeningBehavior == ContainerOpeningBehavior.ClickOrHover; set { if (value) DesktopOpeningBehavior = ContainerOpeningBehavior.ClickOrHover; } }
    public int DesktopContainerOpacityPercent
    {
        get => (int)Math.Round(Config.Settings.DesktopContainerOpacity * 100);
        set => SetPreference(Config.Settings.DesktopContainerOpacity, Math.Clamp(value, 30, 100) / 100.0, v => Config.Settings.DesktopContainerOpacity = v);
    }
    public string DesktopContainerOpacityText => $"{DesktopContainerOpacityPercent}%";
    public bool LockDesktopContainers
    {
        get => Config.Settings.LockDesktopContainers;
        set => SetPreference(Config.Settings.LockDesktopContainers, value, v => Config.Settings.LockDesktopContainers = v);
    }
    public void ResetDesktopPosition()
    {
        Config.Settings.DesktopDockX = 0.82;
        Config.Settings.DesktopDockY = 0.12;
        Config.Settings.DesktopRecycleBinX = 0.015;
        Config.Settings.DesktopRecycleBinY = 0.02;
        foreach (var folder in Config.Folders.Concat(Config.DesktopFolders))
        {
            folder.DesktopX = null;
            folder.DesktopY = null;
        }
        Persist();
    }
    public bool IsTaskbarOnlyActive
    {
        get => Config.Settings.EnableTaskbar && !Config.Settings.EnableEdgeSidebar;
        set { if (value) SetActivationSurfaces(true, false, Config.Settings.EnableDesktopContainers); }
    }
    public bool IsSidebarOnlyActive
    {
        get => !Config.Settings.EnableTaskbar && Config.Settings.EnableEdgeSidebar;
        set { if (value) SetActivationSurfaces(false, true, Config.Settings.EnableDesktopContainers); }
    }
    public bool IsBothSurfacesActive
    {
        get => Config.Settings.EnableTaskbar && Config.Settings.EnableEdgeSidebar;
        set { if (value) SetActivationSurfaces(true, true, Config.Settings.EnableDesktopContainers); }
    }
    public LayoutMode SidebarLayoutMode
    {
        get => Config.Settings.SidebarLayoutMode;
        set => SetPreference(Config.Settings.SidebarLayoutMode, value, v => Config.Settings.SidebarLayoutMode = v);
    }
    public bool IsSidebarSingleLayout
    {
        get => Config.Settings.SidebarLayoutMode == LayoutMode.SingleAppBox;
        set { if (value) SidebarLayoutMode = LayoutMode.SingleAppBox; }
    }
    public bool IsSidebarMultipleLayout
    {
        get => Config.Settings.SidebarLayoutMode == LayoutMode.MultipleAppBoxes;
        set { if (value) SidebarLayoutMode = LayoutMode.MultipleAppBoxes; }
    }
    public ScreenEdge SidebarEdge { get => Config.Settings.SidebarEdge; set => SetPreference(SidebarEdge, value, v => Config.Settings.SidebarEdge = v); }
    public bool IsSidebarRightEdge { get => Config.Settings.SidebarEdge == ScreenEdge.Right; set { if (value) SidebarEdge = ScreenEdge.Right; } }
    public bool IsSidebarLeftEdge { get => Config.Settings.SidebarEdge == ScreenEdge.Left; set { if (value) SidebarEdge = ScreenEdge.Left; } }
    public int SidebarVerticalPercent
    {
        get => (int)Math.Round(Config.Settings.SidebarVerticalPosition * 100);
        set => SetPreference(Config.Settings.SidebarVerticalPosition, Math.Clamp(value, 5, 95) / 100.0, v => Config.Settings.SidebarVerticalPosition = v);
    }
    public int SidebarHandleHeight
    {
        get => Config.Settings.SidebarHandleHeight;
        set => SetPreference(Config.Settings.SidebarHandleHeight, Math.Clamp(value, 48, 320), v => Config.Settings.SidebarHandleHeight = v);
    }
    public int SidebarHandleWidth
    {
        get => Config.Settings.SidebarHandleWidth;
        set => SetPreference(Config.Settings.SidebarHandleWidth, Math.Clamp(value, 3, 16), v => Config.Settings.SidebarHandleWidth = v);
    }
    public int SidebarHandleOpacityPercent
    {
        get => (int)Math.Round(Config.Settings.SidebarHandleOpacity * 100);
        set => SetPreference(Config.Settings.SidebarHandleOpacity, Math.Clamp(value, 0, 100) / 100.0, v => Config.Settings.SidebarHandleOpacity = v);
    }
    public string SidebarHandleOpacityText => SidebarHandleOpacityPercent == 0 ? LocalizationService.Get("Sidebar.HandleOpacityZero") : $"{SidebarHandleOpacityPercent}%";
    public SidebarHandleColorMode SidebarHandleColorMode
    {
        get => Config.Settings.SidebarHandleColorMode;
        set => SetPreference(Config.Settings.SidebarHandleColorMode, value, v => Config.Settings.SidebarHandleColorMode = v);
    }
    public bool IsHandleColorThemeAccent { get => Config.Settings.SidebarHandleColorMode == SidebarHandleColorMode.ThemeAccent; set { if (value) SidebarHandleColorMode = SidebarHandleColorMode.ThemeAccent; } }
    public bool IsHandleColorContainerTint { get => Config.Settings.SidebarHandleColorMode == SidebarHandleColorMode.ContainerTint; set { if (value) SidebarHandleColorMode = SidebarHandleColorMode.ContainerTint; } }
    public bool IsHandleColorCustom { get => Config.Settings.SidebarHandleColorMode == SidebarHandleColorMode.Custom; set { if (value) SidebarHandleColorMode = SidebarHandleColorMode.Custom; } }
    public string SidebarHandleCustomColor
    {
        get => Config.Settings.SidebarHandleCustomColor;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var cleaned = value.Trim();
            if (!cleaned.StartsWith('#')) cleaned = "#" + cleaned;
            if (System.Text.RegularExpressions.Regex.IsMatch(cleaned, @"^#[0-9A-Fa-f]{6}$"))
            {
                var upper = cleaned.ToUpperInvariant();
                var prevColor = Config.Settings.SidebarHandleCustomColor;
                var prevMode = Config.Settings.SidebarHandleColorMode;
                Config.Settings.SidebarHandleCustomColor = upper;
                Config.Settings.SidebarHandleColorMode = SidebarHandleColorMode.Custom;
                try { save(); PreferenceError = ""; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Config.Settings.SidebarHandleCustomColor = prevColor;
                    Config.Settings.SidebarHandleColorMode = prevMode;
                    PreferenceError = "This setting could not be saved: " + ex.Message;
                }
                NotifyPreferences();
            }
        }
    }
    public bool SidebarAutoHideOnFullscreen
    {
        get => Config.Settings.SidebarAutoHideOnFullscreen;
        set => SetPreference(Config.Settings.SidebarAutoHideOnFullscreen, value, v => Config.Settings.SidebarAutoHideOnFullscreen = v);
    }
    public SidebarMonitorMode SidebarMonitorMode
    {
        get => Config.Settings.SidebarMonitorMode;
        set => SetPreference(Config.Settings.SidebarMonitorMode, value, v => Config.Settings.SidebarMonitorMode = v);
    }
    public bool IsSidebarPrimaryMonitor { get => Config.Settings.SidebarMonitorMode == SidebarMonitorMode.PrimaryMonitor; set { if (value) SidebarMonitorMode = SidebarMonitorMode.PrimaryMonitor; } }
    public bool IsSidebarCursorMonitor { get => Config.Settings.SidebarMonitorMode == SidebarMonitorMode.CursorMonitor; set { if (value) SidebarMonitorMode = SidebarMonitorMode.CursorMonitor; } }
    public bool IsSidebarSpecificMonitor { get => Config.Settings.SidebarMonitorMode == SidebarMonitorMode.SpecificMonitor; set { if (value) SidebarMonitorMode = SidebarMonitorMode.SpecificMonitor; } }
    public int SidebarMonitorIndex
    {
        get => Config.Settings.SidebarMonitorIndex;
        set => SetPreference(Config.Settings.SidebarMonitorIndex, Math.Clamp(value, 0, 15), v => Config.Settings.SidebarMonitorIndex = v);
    }
    public ObservableCollection<MonitorOption> AvailableMonitors { get; } = [];
    public MonitorOption? SelectedMonitorOption
    {
        get => AvailableMonitors.FirstOrDefault(m => m.Index == Config.Settings.SidebarMonitorIndex) ?? AvailableMonitors.FirstOrDefault();
        set { if (value is not null) SidebarMonitorIndex = value.Index; }
    }
    public bool SidebarShowRecentApps
    {
        get => Config.Settings.SidebarShowRecentApps;
        set => SetPreference(Config.Settings.SidebarShowRecentApps, value, v => Config.Settings.SidebarShowRecentApps = v);
    }
    public int SidebarRecentAppsCount
    {
        get => Config.Settings.SidebarRecentAppsCount;
        set => SetPreference(Config.Settings.SidebarRecentAppsCount, Math.Clamp(value, 1, 5), v => Config.Settings.SidebarRecentAppsCount = v);
    }
    public ContainerOpeningBehavior TaskbarOpeningBehavior
    {
        get => Config.Settings.OpeningBehavior;
        set => SetPreference(Config.Settings.OpeningBehavior, value, v => Config.Settings.OpeningBehavior = v);
    }
    public bool IsTaskbarClickOpen { get => Config.Settings.OpeningBehavior == ContainerOpeningBehavior.Click; set { if (value) TaskbarOpeningBehavior = ContainerOpeningBehavior.Click; } }
    public bool IsTaskbarHoverOpen { get => Config.Settings.OpeningBehavior == ContainerOpeningBehavior.Hover; set { if (value) TaskbarOpeningBehavior = ContainerOpeningBehavior.Hover; } }
    public bool IsTaskbarClickOrHoverOpen { get => Config.Settings.OpeningBehavior == ContainerOpeningBehavior.ClickOrHover; set { if (value) TaskbarOpeningBehavior = ContainerOpeningBehavior.ClickOrHover; } }

    public bool EnableTaskbarMagnification
    {
        get => Config.Settings.EnableTaskbarMagnification;
        set => SetPreference(Config.Settings.EnableTaskbarMagnification, value, v => Config.Settings.EnableTaskbarMagnification = v);
    }
    public bool IsTaskbarMagnifyAll
    {
        get => Config.Settings.TaskbarMagnificationScope == TaskbarMagnificationScope.AllTaskbarIcons;
        set
        {
            if (value && Config.Settings.TaskbarMagnificationScope != TaskbarMagnificationScope.AllTaskbarIcons)
            {
                SetPreference(Config.Settings.TaskbarMagnificationScope, TaskbarMagnificationScope.AllTaskbarIcons, v => Config.Settings.TaskbarMagnificationScope = v);
                Changed(nameof(IsTaskbarMagnifyAll));
                Changed(nameof(IsTaskbarMagnifyAppDockOnly));
            }
        }
    }
    public bool IsTaskbarMagnifyAppDockOnly
    {
        get => Config.Settings.TaskbarMagnificationScope == TaskbarMagnificationScope.AppDockOnly;
        set
        {
            if (value && Config.Settings.TaskbarMagnificationScope != TaskbarMagnificationScope.AppDockOnly)
            {
                SetPreference(Config.Settings.TaskbarMagnificationScope, TaskbarMagnificationScope.AppDockOnly, v => Config.Settings.TaskbarMagnificationScope = v);
                Changed(nameof(IsTaskbarMagnifyAll));
                Changed(nameof(IsTaskbarMagnifyAppDockOnly));
            }
        }
    }
    public double TaskbarMagnificationScale
    {
        get => Config.Settings.TaskbarMagnificationScale;
        set
        {
            double clamped = Math.Clamp(value, 1.25, 1.85);
            SetPreference(Config.Settings.TaskbarMagnificationScale, clamped, v =>
            {
                Config.Settings.TaskbarMagnificationScale = v;
                Changed(nameof(TaskbarMagnificationScaleText));
            });
        }
    }
    public string TaskbarMagnificationScaleText =>
        LocalizationService.Format("Taskbar.MagnificationScale", TaskbarMagnificationScale);

    public bool ShowTaskbarMagnificationLabel
    {
        get => Config.Settings.ShowTaskbarMagnificationLabel;
        set => SetPreference(Config.Settings.ShowTaskbarMagnificationLabel, value, v => Config.Settings.ShowTaskbarMagnificationLabel = v);
    }
    public ContainerOpeningBehavior SidebarOpeningBehavior
    {
        get => Config.Settings.SidebarOpeningBehavior;
        set => SetPreference(Config.Settings.SidebarOpeningBehavior, value, v => Config.Settings.SidebarOpeningBehavior = v);
    }
    public bool IsSidebarClickOpen { get => Config.Settings.SidebarOpeningBehavior == ContainerOpeningBehavior.Click; set { if (value) SidebarOpeningBehavior = ContainerOpeningBehavior.Click; } }
    public bool IsSidebarHoverOpen { get => Config.Settings.SidebarOpeningBehavior == ContainerOpeningBehavior.Hover; set { if (value) SidebarOpeningBehavior = ContainerOpeningBehavior.Hover; } }
    public bool IsSidebarClickOrHoverOpen { get => Config.Settings.SidebarOpeningBehavior == ContainerOpeningBehavior.ClickOrHover; set { if (value) SidebarOpeningBehavior = ContainerOpeningBehavior.ClickOrHover; } }
    public int HoverOpeningDelayMs
    {
        get => Config.Settings.HoverOpeningDelayMs;
        set => SetPreference(Config.Settings.HoverOpeningDelayMs, Math.Clamp(value, 100, 2000), v => Config.Settings.HoverOpeningDelayMs = v);
    }
    public int HoverClosingDelayMs
    {
        get => Config.Settings.HoverClosingDelayMs;
        set => SetPreference(Config.Settings.HoverClosingDelayMs, Math.Clamp(value, 100, 2000), v => Config.Settings.HoverClosingDelayMs = v);
    }
    public bool StartWithWindows
    {
        get => Config.Settings.StartWithWindows;
        set
        {
            if (value == StartWithWindows) return;
            var previous = StartWithWindows;
            try { writeStartup(value); Config.Settings.StartWithWindows = readStartup(); save(); PreferenceError = ""; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                try { writeStartup(previous); Config.Settings.StartWithWindows = readStartup(); }
                catch { Config.Settings.StartWithWindows = previous; }
                PreferenceError = "Startup preference could not be saved: " + ex.Message;
            }
            NotifyPreferences();
        }
    }
    private void SetPreference<T>(T previous, T value, Action<T> assign)
    {
        if (EqualityComparer<T>.Default.Equals(previous, value)) return;
        assign(value);
        try { save(); PreferenceError = ""; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { assign(previous); PreferenceError = "This setting could not be saved: " + ex.Message; }
        NotifyPreferences();
    }
    private void SetActivationSurfaces(bool taskbar, bool sidebar, bool? desktop = null)
    {
        bool desktopVal = desktop ?? Config.Settings.EnableDesktopContainers;
        if (!taskbar && !sidebar && !desktopVal) taskbar = true;
        if (Config.Settings.EnableTaskbar == taskbar
            && Config.Settings.EnableEdgeSidebar == sidebar
            && Config.Settings.EnableDesktopContainers == desktopVal) return;
        var prevTaskbar = Config.Settings.EnableTaskbar;
        var prevSidebar = Config.Settings.EnableEdgeSidebar;
        var prevDesktop = Config.Settings.EnableDesktopContainers;
        Config.Settings.EnableTaskbar = taskbar;
        Config.Settings.EnableEdgeSidebar = sidebar;
        Config.Settings.EnableDesktopContainers = desktopVal;
        try { save(); PreferenceError = ""; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Config.Settings.EnableTaskbar = prevTaskbar;
            Config.Settings.EnableEdgeSidebar = prevSidebar;
            Config.Settings.EnableDesktopContainers = prevDesktop;
            PreferenceError = "This setting could not be saved: " + ex.Message;
        }
        NotifyPreferences();
    }
    private void NotifyPreferences()
    {
        foreach (var property in new[] { nameof(Language), nameof(SelectedLanguageOption), nameof(IsSingleLayout), nameof(IsMultipleLayout), nameof(ShowAppNames), nameof(ShortenAppNames), nameof(SortMode), nameof(IsManualSort), nameof(IsRecentFirstSort), nameof(GridColumns), nameof(GridRows), nameof(AutoCloseAfterLaunch), nameof(EnableAnimations), nameof(AnimationStyle), nameof(IsGenieAnimation), nameof(IsSuckAnimation), nameof(IsScaleAnimation), nameof(IsMagicLampAnimation), nameof(IsOrigamiAnimation), nameof(IsPageCurlAnimation), nameof(IsBlackHoleAnimation), nameof(IsFlip3DAnimation), nameof(IsJellyAnimation), nameof(IsRandomAnimation), nameof(AnimationDurationMs), nameof(EnableDockMagnification), nameof(EnableLaunchBounce), nameof(EnableCardTiltOnHover), nameof(EnablePoofOnRemove), nameof(AnimationStyleTitle), nameof(AnimationStyleTagline), nameof(AnimationStyleDescription), nameof(StartWithWindows), nameof(UseWindows11Style), nameof(ShowContainerBorder), nameof(AllowCommandLineShells), nameof(EnableTaskbar), nameof(EnableEdgeSidebar), nameof(EnableDesktopContainers), nameof(HideDesktopIcons), nameof(RestoreDesktopIconsOnExit), nameof(DesktopDisplayStyle), nameof(IsDesktopCompactFlyout), nameof(IsDesktopExpandedBoxes), nameof(DesktopContentSource), nameof(IsDesktopSharedContent), nameof(IsDesktopSeparateContent), nameof(DesktopOpeningBehavior), nameof(IsDesktopClickOpen), nameof(IsDesktopHoverOpen), nameof(IsDesktopClickOrHoverOpen), nameof(DesktopContainerOpacityPercent), nameof(DesktopContainerOpacityText), nameof(LockDesktopContainers), nameof(IsTaskbarOnlyActive), nameof(IsSidebarOnlyActive), nameof(IsBothSurfacesActive), nameof(TaskbarOpeningBehavior), nameof(IsTaskbarClickOpen), nameof(IsTaskbarHoverOpen), nameof(IsTaskbarClickOrHoverOpen), nameof(EnableTaskbarMagnification), nameof(IsTaskbarMagnifyAll), nameof(IsTaskbarMagnifyAppDockOnly), nameof(TaskbarMagnificationScale), nameof(TaskbarMagnificationScaleText), nameof(ShowTaskbarMagnificationLabel), nameof(SidebarLayoutMode), nameof(IsSidebarSingleLayout), nameof(IsSidebarMultipleLayout), nameof(SidebarEdge), nameof(IsSidebarRightEdge), nameof(IsSidebarLeftEdge), nameof(SidebarVerticalPercent), nameof(SidebarHandleHeight), nameof(SidebarHandleWidth), nameof(SidebarHandleOpacityPercent), nameof(SidebarHandleOpacityText), nameof(SidebarHandleColorMode), nameof(IsHandleColorThemeAccent), nameof(IsHandleColorContainerTint), nameof(IsHandleColorCustom), nameof(SidebarHandleCustomColor), nameof(SidebarAutoHideOnFullscreen), nameof(SidebarMonitorMode), nameof(IsSidebarPrimaryMonitor), nameof(IsSidebarCursorMonitor), nameof(IsSidebarSpecificMonitor), nameof(SidebarMonitorIndex), nameof(SelectedMonitorOption), nameof(SidebarShowRecentApps), nameof(SidebarRecentAppsCount), nameof(SidebarOpeningBehavior), nameof(IsSidebarClickOpen), nameof(IsSidebarHoverOpen), nameof(IsSidebarClickOrHoverOpen), nameof(HoverOpeningDelayMs), nameof(HoverClosingDelayMs) }) Changed(property);
    }
    private readonly FolderManager manager;
    private readonly FolderManager desktopManager;
    private readonly Action save;
    private AppFolder? selectedFolder;
    public ObservableCollection<AppFolder> Folders { get; } = [];

    private AppItemViewModel? selectedApplication;
    public ObservableCollection<AppItemViewModel> Applications { get; } = [];
    public AppItemViewModel? SelectedApplication
    {
        get => selectedApplication;
        set
        {
            if (!ReferenceEquals(selectedApplication, value))
            {
                selectedApplication = value;
                Changed();
                Changed(nameof(HasSelectedApplication));
                Changed(nameof(SelectedAppIndex));
                Changed(nameof(SelectedAppPositionText));
                Changed(nameof(CanMoveUp));
                Changed(nameof(CanMoveDown));
            }
        }
    }
    public bool HasSelectedApplication => selectedApplication is not null;
    public int SelectedAppIndex => selectedApplication?.Index ?? 0;
    public string SelectedAppPositionText =>
        (selectedFolder is not null && selectedApplication is not null && selectedFolder.Applications.Count > 0)
            ? LocalizationService.Format("Containers.AppPos.Format", selectedApplication.Index, selectedFolder.Applications.Count)
            : "—";
    public bool CanMoveUp => selectedApplication is not null && selectedApplication.Index > 1;
    public bool CanMoveDown => selectedApplication is not null && selectedFolder is not null && selectedApplication.Index < selectedFolder.Applications.Count;
    public AppFolder? SelectedFolder
    {
        get => selectedFolder;
        set
        {
            selectedFolder = value;
            Config.SelectedFolderId = value?.Id;
            Changed();
            Changed(nameof(FolderName));
            Changed(nameof(HasSelectedFolder));
            Changed(nameof(SelectedContainerAppsText));
    
            Changed(nameof(SelectedContainerStatusText));
            Changed(nameof(IsSelectedContainerActive));
            Changed(nameof(CanMoveFolderUp));
            Changed(nameof(CanMoveFolderDown));
            Changed(nameof(SelectedContainerPositionText));
            Changed(nameof(SelectedFolderSortOverride));
            Changed(nameof(SelectedFolderSortOverrideIndex));
            Changed(nameof(SelectedFolderColumnsOverride));
            Changed(nameof(SelectedFolderColumnsOverrideIndex));
            Changed(nameof(SelectedFolderRowsOverride));
            Changed(nameof(SelectedFolderRowsOverrideIndex));
            RefreshApps();
        }
    }
    public bool HasSelectedFolder => selectedFolder is not null;
    public bool IsSelectedContainerActive => selectedFolder?.IsActive == true;
    public string FolderName { get => selectedFolder?.Name ?? ""; set { if (selectedFolder is not null) { manager.Rename(selectedFolder, value); Changed(); } } }
    public string SelectedContainerAppsText => selectedFolder is null
        ? LocalizationService.Get("Containers.AppsCount.Zero")
        : (selectedFolder.Applications.Count == 1 ? LocalizationService.Get("Containers.AppsCount.Single") : LocalizationService.Format("Containers.AppsCount.Multiple", selectedFolder.Applications.Count));
    public string SelectedContainerStatusText => selectedFolder is null
        ? LocalizationService.Get("Containers.Status.NotActivated")
        : (selectedFolder.IsActive ? LocalizationService.Get("Containers.Status.Activated") : LocalizationService.Get("Containers.Status.NotActivated"));
    public bool CanMoveFolderUp => selectedFolder is not null && Config.Folders.IndexOf(selectedFolder) > 0;
    public bool CanMoveFolderDown => selectedFolder is not null && Config.Folders.IndexOf(selectedFolder) >= 0 && Config.Folders.IndexOf(selectedFolder) < Config.Folders.Count - 1;
    public string SelectedContainerPositionText =>
        (selectedFolder is not null && Config.Folders.Count > 0 && Config.Folders.IndexOf(selectedFolder) >= 0)
            ? LocalizationService.Format("Containers.ContainerPos.Format", Config.Folders.IndexOf(selectedFolder) + 1, Config.Folders.Count)
            : "—";

    public SortMode? SelectedFolderSortOverride
    {
        get => selectedFolder?.SortModeOverride;
        set
        {
            if (selectedFolder is null || selectedFolder.SortModeOverride == value) return;
            selectedFolder.SortModeOverride = value;
            save();
            Changed();
            Changed(nameof(SelectedFolderSortOverrideIndex));
        }
    }
    public int SelectedFolderSortOverrideIndex
    {
        get => selectedFolder?.SortModeOverride switch
        {
            SortMode.Manual => 1,
            SortMode.RecentFirst => 2,
            _ => 0
        };
        set => SelectedFolderSortOverride = value switch
        {
            1 => SortMode.Manual,
            2 => SortMode.RecentFirst,
            _ => null
        };
    }

    public int? SelectedFolderColumnsOverride
    {
        get => selectedFolder?.GridColumnsOverride;
        set
        {
            var clamped = value.HasValue ? Math.Clamp(value.Value, 2, 8) : (int?)null;
            if (selectedFolder is null || selectedFolder.GridColumnsOverride == clamped) return;
            selectedFolder.GridColumnsOverride = clamped;
            save();
            Changed();
            Changed(nameof(SelectedFolderColumnsOverrideIndex));
        }
    }
    public int SelectedFolderColumnsOverrideIndex
    {
        get => selectedFolder?.GridColumnsOverride is { } cols ? Math.Clamp(cols - 1, 1, 7) : 0;
        set => SelectedFolderColumnsOverride = value is >= 1 and <= 7 ? value + 1 : null;
    }

    public int? SelectedFolderRowsOverride
    {
        get => selectedFolder?.GridRowsOverride;
        set
        {
            var clamped = value.HasValue ? Math.Clamp(value.Value, 1, 8) : (int?)null;
            if (selectedFolder is null || selectedFolder.GridRowsOverride == clamped) return;
            selectedFolder.GridRowsOverride = clamped;
            save();
            Changed();
            Changed(nameof(SelectedFolderRowsOverrideIndex));
        }
    }
    public int SelectedFolderRowsOverrideIndex
    {
        get => selectedFolder?.GridRowsOverride is { } rows ? Math.Clamp(rows, 1, 8) : 0;
        set => SelectedFolderRowsOverride = value is >= 1 and <= 8 ? value : null;
    }

    // --- Separate Desktop Containers Management ---
    private AppFolder? selectedDesktopFolder;
    public ObservableCollection<AppFolder> DesktopFolders { get; } = [];
    private AppItemViewModel? selectedDesktopApplication;
    public ObservableCollection<AppItemViewModel> DesktopApplications { get; } = [];

    public AppFolder? SelectedDesktopFolder
    {
        get => selectedDesktopFolder;
        set
        {
            selectedDesktopFolder = value;
            Config.SelectedDesktopFolderId = value?.Id;
            Changed();
            Changed(nameof(DesktopFolderName));
            Changed(nameof(HasSelectedDesktopFolder));
            Changed(nameof(SelectedDesktopContainerAppsText));
            Changed(nameof(SelectedDesktopContainerStatusText));
            Changed(nameof(IsSelectedDesktopContainerActive));
            Changed(nameof(CanMoveDesktopFolderUp));
            Changed(nameof(CanMoveDesktopFolderDown));
            Changed(nameof(SelectedDesktopContainerPositionText));
            RefreshDesktopApps();
        }
    }
    public bool HasSelectedDesktopFolder => selectedDesktopFolder is not null;
    public bool IsSelectedDesktopContainerActive => selectedDesktopFolder?.IsActive == true;
    public string DesktopFolderName
    {
        get => selectedDesktopFolder?.Name ?? "";
        set
        {
            if (selectedDesktopFolder is not null)
            {
                desktopManager.Rename(selectedDesktopFolder, value);
                Changed();
            }
        }
    }
    public string SelectedDesktopContainerAppsText => selectedDesktopFolder is null
        ? LocalizationService.Get("Containers.AppsCount.Zero")
        : (selectedDesktopFolder.Applications.Count == 1
            ? LocalizationService.Get("Containers.AppsCount.Single")
            : LocalizationService.Format("Containers.AppsCount.Multiple", selectedDesktopFolder.Applications.Count));
    public string SelectedDesktopContainerStatusText => selectedDesktopFolder is null
        ? LocalizationService.Get("Containers.Status.NotActivated")
        : (selectedDesktopFolder.IsActive
            ? LocalizationService.Get("Containers.Status.Activated")
            : LocalizationService.Get("Containers.Status.NotActivated"));
    public bool CanMoveDesktopFolderUp => selectedDesktopFolder is not null && Config.DesktopFolders.IndexOf(selectedDesktopFolder) > 0;
    public bool CanMoveDesktopFolderDown => selectedDesktopFolder is not null && Config.DesktopFolders.IndexOf(selectedDesktopFolder) >= 0 && Config.DesktopFolders.IndexOf(selectedDesktopFolder) < Config.DesktopFolders.Count - 1;
    public string SelectedDesktopContainerPositionText =>
        (selectedDesktopFolder is not null && Config.DesktopFolders.Count > 0 && Config.DesktopFolders.IndexOf(selectedDesktopFolder) >= 0)
            ? LocalizationService.Format("Containers.ContainerPos.Format", Config.DesktopFolders.IndexOf(selectedDesktopFolder) + 1, Config.DesktopFolders.Count)
            : "—";

    public AppItemViewModel? SelectedDesktopApplication
    {
        get => selectedDesktopApplication;
        set
        {
            if (!ReferenceEquals(selectedDesktopApplication, value))
            {
                selectedDesktopApplication = value;
                Changed();
                Changed(nameof(HasSelectedDesktopApplication));
                Changed(nameof(SelectedDesktopAppPositionText));
                Changed(nameof(CanMoveDesktopAppUp));
                Changed(nameof(CanMoveDesktopAppDown));
            }
        }
    }
    public bool HasSelectedDesktopApplication => selectedDesktopApplication is not null;
    public string SelectedDesktopAppPositionText =>
        (selectedDesktopFolder is not null && selectedDesktopApplication is not null && selectedDesktopFolder.Applications.Count > 0)
            ? LocalizationService.Format("Containers.AppPos.Format", selectedDesktopApplication.Index, selectedDesktopFolder.Applications.Count)
            : "—";
    public bool CanMoveDesktopAppUp => selectedDesktopApplication is not null && selectedDesktopApplication.Index > 1;
    public bool CanMoveDesktopAppDown => selectedDesktopApplication is not null && selectedDesktopFolder is not null && selectedDesktopApplication.Index < selectedDesktopFolder.Applications.Count;

    public string AppVersionText
    {
        get
        {
            var v = typeof(SettingsViewModel).Assembly.GetName().Version;
            return v is not null ? $"Version {v.Major}.{v.Minor}.{Math.Max(0, v.Build)}" : "Version 3.1.0";
        }
    }

    public string ActiveStorageDirectory => AppPaths.ActiveDataDirectory;
    public string PortableStorageDirectory => AppPaths.PortableDataDirectory;
    public string AppDataStorageDirectory => AppPaths.LocalAppDataDirectory;
    public bool IsPortableMode => AppPaths.IsPortableMode;
    public string ActiveStorageModeText => IsPortableMode
        ? LocalizationService.Get("Portable.Mode.Portable")
        : LocalizationService.Get("Portable.Mode.AppData");

    public void NotifyStorageLocationChanged()
    {
        Changed(nameof(ActiveStorageDirectory));
        Changed(nameof(PortableStorageDirectory));
        Changed(nameof(AppDataStorageDirectory));
        Changed(nameof(IsPortableMode));
        Changed(nameof(ActiveStorageModeText));
        Refresh();
    }

    public SettingsViewModel(Configuration config, Action save, Action<bool>? writeStartup = null, Func<bool>? readStartup = null, bool? windows11StyleAvailable = null)
    {
        Config = config;
        manager = new(config);
        desktopManager = new(config, useDesktopFolders: true);
        this.save = save;
        this.writeStartup = writeStartup ?? StartupRegistration.SetEnabled;
        this.readStartup = readStartup ?? StartupRegistration.IsEnabled;
        Windows11StyleAvailable = windows11StyleAvailable ?? OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
        IsFencesDetected = DesktopIconsService.IsStardockFencesDetected();
        PopulateLanguages();
        PopulateMonitors();
        LocalizationService.LanguageChanged += OnLanguageChanged;
        Refresh();
    }
    private void PopulateMonitors()
    {
        AvailableMonitors.Clear();
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                var primaryTag = s.Primary ? " (Primary)" : "";
                AvailableMonitors.Add(new MonitorOption(i, $"Display {i + 1}{primaryTag} — {s.Bounds.Width}×{s.Bounds.Height}"));
            }
        }
        catch
        {
        }
        if (AvailableMonitors.Count == 0)
        {
            AvailableMonitors.Add(new MonitorOption(0, "Display 1 (Primary)"));
        }
    }
    private void PopulateLanguages()
    {
        AvailableLanguages.Clear();
        foreach (var lang in LocalizationService.SupportedLanguages)
        {
            AvailableLanguages.Add(new LanguageOption(lang.Language, lang.DisplayName));
        }
    }
    private void OnLanguageChanged()
    {
        Changed(nameof(Language));
        Changed(nameof(SelectedLanguageOption));
        Changed(nameof(SelectedContainerAppsText));
        Changed(nameof(SelectedContainerStatusText));
        Changed(nameof(SelectedContainerPositionText));
        Changed(nameof(SelectedAppPositionText));
        Changed(nameof(SelectedDesktopContainerAppsText));
        Changed(nameof(SelectedDesktopContainerStatusText));
        Changed(nameof(SelectedDesktopContainerPositionText));
        Changed(nameof(SelectedDesktopAppPositionText));
        Changed(nameof(SidebarHandleOpacityText));
        Changed(nameof(DesktopContainerOpacityText));
        Changed(nameof(AppVersionText));
        Changed(nameof(AnimationStyleTitle));
        Changed(nameof(AnimationStyleTagline));
        Changed(nameof(AnimationStyleDescription));
        Changed(nameof(ActiveStorageModeText));
        PopulateMonitors();
    }
    public void ToggleActive() { if (SelectedFolder is not null) { SelectedFolder.IsActive = !SelectedFolder.IsActive; Persist(); } }
    public void Create(string name) { manager.Create(name); Persist(); }
    public void Rename(string name) { if (SelectedFolder is not null) { manager.Rename(SelectedFolder, name); Persist(); } }
    public void Delete() { if (SelectedFolder is not null) { manager.Delete(SelectedFolder); Persist(); } }
    public void Add(AppEntry entry)
    {
        if (SelectedFolder is null) throw new ArgumentException(LocalizationService.Get("Dialog.CreateOrSelectContainerFirst"));
        var result = new ApplicationImportService(new WindowsShortcutResolver(), null, () => Config.Settings.AllowCommandLineShells).Import(Config, SelectedFolder, [entry.Kind == TargetKind.Packaged ? "shell:AppsFolder\\" + entry.Target : entry.Target]);
        if (result.Rejected.Count > 0) throw new ArgumentException(result.Rejected[0].Reason);
        Persist();
    }
    public void Remove()
    {
        if (SelectedFolder is not null && SelectedApplication is not null)
        {
            var entry = SelectedApplication.Entry;
            var oldIndex = SelectedFolder.Applications.IndexOf(entry);
            manager.Remove(SelectedFolder, entry);
            save();
            var nextIndex = Math.Min(oldIndex, SelectedFolder.Applications.Count - 1);
            var nextTarget = nextIndex >= 0 ? SelectedFolder.Applications[nextIndex] : null;
            RefreshAppsRetainingSelection(nextTarget);
        }
    }
    public void Move(int offset)
    {
        if (SelectedFolder is not null && SelectedApplication is not null)
        {
            var entry = SelectedApplication.Entry;
            manager.Move(SelectedFolder, entry, offset);
            save();
            RefreshAppsRetainingSelection(entry);
        }
    }
    public void MoveToTop()
    {
        if (SelectedFolder is not null && SelectedApplication is not null)
        {
            var entry = SelectedApplication.Entry;
            manager.MoveToTop(SelectedFolder, entry);
            save();
            RefreshAppsRetainingSelection(entry);
        }
    }
    public void MoveToBottom()
    {
        if (SelectedFolder is not null && SelectedApplication is not null)
        {
            var entry = SelectedApplication.Entry;
            manager.MoveToBottom(SelectedFolder, entry);
            save();
            RefreshAppsRetainingSelection(entry);
        }
    }
    public void MoveFolder(int offset)
    {
        if (SelectedFolder is not { } folder) return;
        manager.MoveFolder(folder, offset);
        Persist();
    }
    public void MoveFolderToTop()
    {
        if (SelectedFolder is not { } folder) return;
        manager.MoveFolderToTop(folder);
        Persist();
    }
    public void MoveFolderToBottom()
    {
        if (SelectedFolder is not { } folder) return;
        manager.MoveFolderToBottom(folder);
        Persist();
    }
    public void ReorderFolder(AppFolder source, AppFolder target)
    {
        if (ReferenceEquals(source, target)) return;
        var targetIndex = Config.Folders.IndexOf(target);
        if (targetIndex < 0) return;
        manager.ReorderFolder(source, targetIndex);
        Config.SelectedFolderId = source.Id;
        Persist();
    }

    // --- Separate Desktop Containers Commands ---
    public void ToggleDesktopFolderActive()
    {
        if (SelectedDesktopFolder is not null)
        {
            SelectedDesktopFolder.IsActive = !SelectedDesktopFolder.IsActive;
            Persist();
        }
    }
    public void CreateDesktopFolder(string name)
    {
        desktopManager.Create(name);
        Persist();
    }
    public void RenameDesktopFolder(string name)
    {
        if (SelectedDesktopFolder is not null)
        {
            desktopManager.Rename(SelectedDesktopFolder, name);
            Persist();
        }
    }
    public void DeleteDesktopFolder()
    {
        if (SelectedDesktopFolder is not null)
        {
            desktopManager.Delete(SelectedDesktopFolder);
            Persist();
        }
    }
    public void MoveDesktopFolder(int offset)
    {
        if (SelectedDesktopFolder is not { } folder) return;
        desktopManager.MoveFolder(folder, offset);
        Persist();
    }
    public void ReorderDesktopFolder(AppFolder source, AppFolder target)
    {
        if (ReferenceEquals(source, target)) return;
        var targetIndex = Config.DesktopFolders.IndexOf(target);
        if (targetIndex < 0) return;
        desktopManager.ReorderFolder(source, targetIndex);
        Config.SelectedDesktopFolderId = source.Id;
        Persist();
    }
    public void AddDesktopApp(AppEntry entry)
    {
        if (SelectedDesktopFolder is null)
        {
            SelectedDesktopFolder = desktopManager.Create(LocalizationService.Get("Desktop.DefaultContainerName"));
        }
        var result = new ApplicationImportService(new WindowsShortcutResolver(), null, () => Config.Settings.AllowCommandLineShells)
            .Import(Config, SelectedDesktopFolder, [entry.Kind == TargetKind.Packaged ? "shell:AppsFolder\\" + entry.Target : entry.Target]);
        if (result.Rejected.Count > 0) throw new ArgumentException(result.Rejected[0].Reason);
        Persist();
    }
    public void RemoveDesktopApp()
    {
        if (SelectedDesktopFolder is not null && SelectedDesktopApplication is not null)
        {
            var entry = SelectedDesktopApplication.Entry;
            var oldIndex = SelectedDesktopFolder.Applications.IndexOf(entry);
            desktopManager.Remove(SelectedDesktopFolder, entry);
            save();
            var nextIndex = Math.Min(oldIndex, SelectedDesktopFolder.Applications.Count - 1);
            var nextTarget = nextIndex >= 0 ? SelectedDesktopFolder.Applications[nextIndex] : null;
            RefreshDesktopAppsRetainingSelection(nextTarget);
        }
    }
    public void MoveDesktopApp(int offset)
    {
        if (SelectedDesktopFolder is not null && SelectedDesktopApplication is not null)
        {
            var entry = SelectedDesktopApplication.Entry;
            desktopManager.Move(SelectedDesktopFolder, entry, offset);
            save();
            RefreshDesktopAppsRetainingSelection(entry);
        }
    }
    public void MoveDesktopAppToTop()
    {
        if (SelectedDesktopFolder is not null && SelectedDesktopApplication is not null)
        {
            var entry = SelectedDesktopApplication.Entry;
            desktopManager.MoveToTop(SelectedDesktopFolder, entry);
            save();
            RefreshDesktopAppsRetainingSelection(entry);
        }
    }
    public void MoveDesktopAppToBottom()
    {
        if (SelectedDesktopFolder is not null && SelectedDesktopApplication is not null)
        {
            var entry = SelectedDesktopApplication.Entry;
            desktopManager.MoveToBottom(SelectedDesktopFolder, entry);
            save();
            RefreshDesktopAppsRetainingSelection(entry);
        }
    }
    public int CopyFromSidebarContainers()
    {
        int copiedContainers = 0;
        var existingNames = new HashSet<string>(Config.DesktopFolders.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var src in Config.Folders)
        {
            var baseName = src.Name;
            var candidate = baseName;
            int suffix = 2;
            while (existingNames.Contains(candidate))
            {
                candidate = $"{baseName} ({suffix++})";
            }
            existingNames.Add(candidate);
            var clone = new AppFolder
            {
                Id = Guid.NewGuid(),
                Name = candidate,
                IsActive = src.IsActive,
                Icon = new ContainerIcon
                {
                    Kind = src.Icon.Kind,
                    BuiltIn = src.Icon.BuiltIn,
                    ResourcePath = src.Icon.ResourcePath
                },
                SortModeOverride = src.SortModeOverride,
                GridColumnsOverride = src.GridColumnsOverride,
                GridRowsOverride = src.GridRowsOverride,
                Applications = src.Applications.Select(a => new AppEntry
                {
                    Id = Guid.NewGuid(),
                    Name = a.Name,
                    Target = a.Target,
                    Kind = a.Kind
                }).ToList()
            };
            Config.DesktopFolders.Add(clone);
            Config.SelectedDesktopFolderId = clone.Id;
            copiedContainers++;
        }
        Persist();
        return copiedContainers;
    }
    public (int Added, int Rejected) ImportDesktopShortcuts()
    {
        var files = DesktopIconsService.DiscoverDesktopShortcutFiles();
        if (files.Count == 0) return (0, 0);
        if (SelectedDesktopFolder is null)
        {
            SelectedDesktopFolder = desktopManager.Create(LocalizationService.Get("Desktop.DefaultContainerName"));
        }
        var importer = new ApplicationImportService(new WindowsShortcutResolver(), null, () => Config.Settings.AllowCommandLineShells);
        var result = importer.Import(Config, SelectedDesktopFolder, files);
        Persist();
        return (result.Added.Count, result.Rejected.Count);
    }

    public void ExportBackup(string destinationPath, string? storeDirectory = null)
    {
        var backupStore = new ConfigurationStore(storeDirectory);
        backupStore.ExportBackup(Config, destinationPath);
    }
    public void ImportBackup(string sourcePath, string? storeDirectory = null)
    {
        var backupStore = new ConfigurationStore(storeDirectory);
        var imported = backupStore.ImportBackup(sourcePath);

        Config.Settings.LayoutMode = imported.Settings.LayoutMode;
        Config.Settings.OpeningBehavior = imported.Settings.OpeningBehavior;
        Config.Settings.HoverOpeningDelayMs = imported.Settings.HoverOpeningDelayMs;
        Config.Settings.HoverClosingDelayMs = imported.Settings.HoverClosingDelayMs;
        Config.Settings.ShowPopupPointer = imported.Settings.ShowPopupPointer;
        Config.Settings.IconSize = imported.Settings.IconSize;
        Config.Settings.GridColumns = imported.Settings.GridColumns;
        Config.Settings.GridRows = imported.Settings.GridRows;
        Config.Settings.ShowApplicationNames = imported.Settings.ShowApplicationNames;
        Config.Settings.ShortenAppNames = imported.Settings.ShortenAppNames;
        Config.Settings.SortMode = imported.Settings.SortMode;
        Config.Settings.CloseAfterLaunch = imported.Settings.CloseAfterLaunch;
        Config.Settings.EnableAnimations = imported.Settings.EnableAnimations;
        Config.Settings.AnimationStyle = imported.Settings.AnimationStyle;
        Config.Settings.AnimationDurationMs = imported.Settings.AnimationDurationMs;
        Config.Settings.EnableDockMagnification = imported.Settings.EnableDockMagnification;
        Config.Settings.EnableLaunchBounce = imported.Settings.EnableLaunchBounce;
        Config.Settings.EnableCardTiltOnHover = imported.Settings.EnableCardTiltOnHover;
        Config.Settings.EnablePoofOnRemove = imported.Settings.EnablePoofOnRemove;
        Config.Settings.UseWindows11Style = imported.Settings.UseWindows11Style;
        Config.Settings.ShowContainerBorder = imported.Settings.ShowContainerBorder;
        Config.Settings.AllowCommandLineShells = imported.Settings.AllowCommandLineShells;
        Config.Settings.EnableTaskbar = imported.Settings.EnableTaskbar;
        Config.Settings.EnableTaskbarMagnification = imported.Settings.EnableTaskbarMagnification;
        Config.Settings.TaskbarMagnificationScope = imported.Settings.TaskbarMagnificationScope;
        Config.Settings.TaskbarMagnificationScale = imported.Settings.TaskbarMagnificationScale;
        Config.Settings.ShowTaskbarMagnificationLabel = imported.Settings.ShowTaskbarMagnificationLabel;
        Config.Settings.EnableEdgeSidebar = imported.Settings.EnableEdgeSidebar;
        Config.Settings.SidebarLayoutMode = imported.Settings.SidebarLayoutMode;
        Config.Settings.SidebarEdge = imported.Settings.SidebarEdge;
        Config.Settings.SidebarVerticalPosition = imported.Settings.SidebarVerticalPosition;
        Config.Settings.SidebarHandleHeight = imported.Settings.SidebarHandleHeight;
        Config.Settings.SidebarHandleWidth = imported.Settings.SidebarHandleWidth;
        Config.Settings.SidebarHandleOpacity = imported.Settings.SidebarHandleOpacity;
        Config.Settings.SidebarHandleColorMode = imported.Settings.SidebarHandleColorMode;
        Config.Settings.SidebarHandleCustomColor = imported.Settings.SidebarHandleCustomColor;
        Config.Settings.SidebarAutoHideOnFullscreen = imported.Settings.SidebarAutoHideOnFullscreen;
        Config.Settings.SidebarMonitorMode = imported.Settings.SidebarMonitorMode;
        Config.Settings.SidebarMonitorIndex = imported.Settings.SidebarMonitorIndex;
        Config.Settings.SidebarShowRecentApps = imported.Settings.SidebarShowRecentApps;
        Config.Settings.SidebarRecentAppsCount = imported.Settings.SidebarRecentAppsCount;
        Config.Settings.SidebarOpeningBehavior = imported.Settings.SidebarOpeningBehavior;
        Config.Settings.EnableDesktopContainers = imported.Settings.EnableDesktopContainers;
        Config.Settings.HideDesktopIcons = imported.Settings.HideDesktopIcons;
        Config.Settings.RestoreDesktopIconsOnExit = imported.Settings.RestoreDesktopIconsOnExit;
        Config.Settings.DesktopContentSource = imported.Settings.DesktopContentSource;
        Config.Settings.DesktopDisplayStyle = imported.Settings.DesktopDisplayStyle;
        Config.Settings.DesktopOpeningBehavior = imported.Settings.DesktopOpeningBehavior;
        Config.Settings.DesktopContainerOpacity = imported.Settings.DesktopContainerOpacity;
        Config.Settings.LockDesktopContainers = imported.Settings.LockDesktopContainers;
        Config.Settings.DesktopDockX = imported.Settings.DesktopDockX;
        Config.Settings.DesktopDockY = imported.Settings.DesktopDockY;
        Config.Settings.Theme = imported.Settings.Theme;
        Config.Settings.Language = imported.Settings.Language;

        Config.Folders.Clear();
        Config.Folders.AddRange(imported.Folders);
        Config.SelectedFolderId = imported.SelectedFolderId;

        Config.DesktopFolders.Clear();
        Config.DesktopFolders.AddRange(imported.DesktopFolders);
        Config.SelectedDesktopFolderId = imported.SelectedDesktopFolderId;

        Persist();
        LanguageResourceService.Apply(Config.Settings.Language);
        NotifyPreferences();
    }
    public void Persist() { save(); Refresh(); }
    private void Refresh()
    {
        var id = Config.SelectedFolderId;
        Folders.Clear(); foreach (var folder in Config.Folders) Folders.Add(folder);
        SelectedFolder = Config.Folders.FirstOrDefault(f => f.Id == id) ?? Config.Folders.FirstOrDefault();
        RefreshDesktop();
    }
    private void RefreshDesktop()
    {
        var id = Config.SelectedDesktopFolderId;
        DesktopFolders.Clear();
        foreach (var folder in Config.DesktopFolders) DesktopFolders.Add(folder);
        SelectedDesktopFolder = Config.DesktopFolders.FirstOrDefault(f => f.Id == id) ?? Config.DesktopFolders.FirstOrDefault();
    }
    private void RefreshApps() => RefreshAppsRetainingSelection(null);
    private void RefreshAppsRetainingSelection(AppEntry? target)
    {
        Applications.Clear();
        AppItemViewModel? match = null;
        if (SelectedFolder is not null)
        {
            int idx = 1;
            foreach (var app in SelectedFolder.Applications)
            {
                var item = new AppItemViewModel(app, idx++);
                Applications.Add(item);
                if (target is not null && app.Id == target.Id)
                {
                    match = item;
                }
            }
        }
        SelectedApplication = match;
        Changed(nameof(SelectedContainerAppsText));

        Changed(nameof(SelectedContainerStatusText));
        Changed(nameof(IsSelectedContainerActive));
    }
    private void RefreshDesktopApps() => RefreshDesktopAppsRetainingSelection(null);
    private void RefreshDesktopAppsRetainingSelection(AppEntry? target)
    {
        DesktopApplications.Clear();
        AppItemViewModel? match = null;
        if (SelectedDesktopFolder is not null)
        {
            int idx = 1;
            foreach (var app in SelectedDesktopFolder.Applications)
            {
                var item = new AppItemViewModel(app, idx++);
                DesktopApplications.Add(item);
                if (target is not null && app.Id == target.Id)
                {
                    match = item;
                }
            }
        }
        SelectedDesktopApplication = match;
        Changed(nameof(SelectedDesktopContainerAppsText));
        Changed(nameof(SelectedDesktopContainerStatusText));
        Changed(nameof(IsSelectedDesktopContainerActive));
    }
}

public sealed class AppItemViewModel : ObservableObject
{
    public AppEntry Entry { get; }
    private int index;
    public int Index
    {
        get => index;
        set
        {
            if (index != value)
            {
                index = value;
                Changed();
                Changed(nameof(IndexBadgeText));
            }
        }
    }
    public string IndexBadgeText => $"#{index}";
    public string Name => Entry.Name;
    public string Target => Entry.Target;
    public TargetKind Kind => Entry.Kind;

    public AppItemViewModel(AppEntry entry, int index)
    {
        Entry = entry;
        this.index = index;
    }
}

public sealed record MonitorOption(int Index, string Label)
{
    public override string ToString() => Label;
}

public sealed record LanguageOption(AppLanguage Language, string DisplayName)
{
    public override string ToString() => DisplayName;
}
