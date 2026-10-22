namespace AppBox.Core;

// Transient interaction state is deliberately excluded from the persisted configuration.
public sealed class LauncherInteraction
{
    public bool IsEditing { get; private set; }
    public bool CanLaunch => !IsEditing;
    public void BeginEdit() => IsEditing = true;
    public void EndEdit() => IsEditing = false;
    public bool DismissOnFocusLost(bool suppressed) => !IsEditing && !suppressed;
    public bool DismissAfterLaunch(bool success, bool automaticClose) => !IsEditing && success && automaticClose;
}

public static class LauncherSizing
{
    public const double Width = 468;
    public const double TileWidth = 96;
    public const double RowHeight = 118;
    public static double GridHeight(int appCount, int columns = 4) => Math.Ceiling(Math.Max(0, appCount) / (double)Math.Max(1, columns)) * RowHeight;
    public static double DesiredHeight(int appCount, bool editing, int columns = 4)
    {
        // Compact grid plus an edit-only Done/status strip.
        return Math.Min(770, 26 + (appCount == 0 ? 48 : GridHeight(appCount, columns)) + (editing ? 124 : 0));
    }
}
