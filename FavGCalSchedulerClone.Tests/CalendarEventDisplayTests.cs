using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarEventDisplayTests
{
    [Fact]
    public void CalendarCellDisplayText_IncludesTimeForTimedEvent()
    {
        var item = new CalendarEvent
        {
            Title = "Meeting",
            Start = new DateTimeOffset(new DateTime(2026, 5, 23, 9, 30, 0)),
            End = new DateTimeOffset(new DateTime(2026, 5, 23, 10, 0, 0))
        };

        Assert.Equal("09:30 Meeting", item.CalendarCellDisplayText);
    }

    [Fact]
    public void CalendarCellDisplayText_ShowsTodoStatePriorityAndProgress()
    {
        var item = new CalendarEvent
        {
            Title = "Confirm",
            Description = "#todoA56%",
            IsAllDay = true,
            IsTodoLike = true,
            Start = new DateTimeOffset(new DateTime(2026, 5, 23))
        };

        Assert.Equal("[ ] [A] 56% Confirm", item.CalendarCellDisplayText);
    }
}
