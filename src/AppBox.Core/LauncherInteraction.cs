namespace AppBox.Core;

// Transient state. No hover timer is scheduled while pinned hover capability is unavailable.
public enum PopupState { Closed, Opening, Open, HoverOpen, Editing, Closing }
public sealed class LauncherInteraction
{
    public PopupState State { get; private set; } = PopupState.Closed;
    private bool editing;
    private long generation;
    public bool IsEditing => editing;
    public bool CanLaunch => !IsEditing;
    public long Generation => generation;
    public bool TryBeginOpen(bool fromHover = false)
    {
        if (fromHover && !TaskbarCapabilities.SupportsPinnedHover) return false;
        if (State is PopupState.Open or PopupState.Editing or PopupState.Opening) return false;
        generation++; State = PopupState.Opening; return true;
    }
    public void CompleteOpen(long token) { if (token == generation && State == PopupState.Opening) State = editing ? PopupState.Editing : PopupState.Open; }
    public void BeginClose() { generation++; State = PopupState.Closing; }
    public void CompleteClose() { generation++; editing = false; State = PopupState.Closed; }
    public void BeginEdit() { generation++; editing = true; State = PopupState.Editing; }
    public void EndEdit() { editing = false; if (State == PopupState.Editing) State = PopupState.Open; }
    public bool DismissOnFocusLost(bool suppressed) => !IsEditing && !suppressed;
    public bool DismissAfterLaunch(bool success, bool automaticClose) => !IsEditing && success && automaticClose;
}

public static class LauncherSizing
{
    public const double Width = 468;
    public const double TileWidth = 96;
    public const double RowHeight = 118;
    public const double HeaderHeight = 38;
    public static int EffectiveColumns(int appCount, int maxColumns = 4) => Math.Min(Math.Clamp(maxColumns, 1, 8), Math.Max(1, appCount));
    public static double GridWidth(int appCount, int maxColumns = 4, int maxRows = 4, double cellWidth = TileWidth)
    {
        var cols = EffectiveColumns(appCount, maxColumns);
        var rows = (int)Math.Ceiling(Math.Max(0, appCount) / (double)cols);
        var needsScrollbar = rows > Math.Clamp(maxRows, 1, 8);
        return Math.Max(314, cols * cellWidth + (needsScrollbar ? 42 : 24));
    }
    public static double GridHeight(int appCount, int columns = 4, double rowHeight = RowHeight) => Math.Ceiling(Math.Max(0, appCount) / (double)Math.Max(1, columns)) * rowHeight;
    public static double DesiredHeight(int appCount, bool editing, int columns = 4, int maxRows = 4, double rowHeight = RowHeight)
    {
        var maxGridHeight = Math.Clamp(maxRows, 1, 8) * rowHeight;
        var visibleGridHeight = appCount == 0 ? 56 : Math.Min(GridHeight(appCount, columns, rowHeight), maxGridHeight);
        return 26 + HeaderHeight + visibleGridHeight + (editing ? 46 : 0);
    }
}
