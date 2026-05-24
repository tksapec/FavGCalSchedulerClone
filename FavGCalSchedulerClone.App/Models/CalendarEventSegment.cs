namespace FavGCalSchedulerClone.App.Models;

public sealed class CalendarEventSegment
{
    public CalendarEvent? Event { get; init; }
    public DateTime Date { get; init; }
    public int Lane { get; init; }
    public bool IsWeekSegmentStart { get; init; }
    public bool IsWeekSegmentEnd { get; init; }
    public bool ShowText { get; init; }
    public bool IsVisible => Event is not null;
    public string DisplayColor => Event?.DisplayColor ?? "Transparent";
    public string DisplayForegroundColor => Event?.DisplayForegroundColor ?? "Transparent";
    public string DisplayText => ShowText ? Event?.CalendarCellDisplayText ?? "" : "";
    public string? ToolTipText => Event?.ToolTipText;

    public static CalendarEventSegment Empty(DateTime date, int lane) => new()
    {
        Date = date,
        Lane = lane
    };
}
