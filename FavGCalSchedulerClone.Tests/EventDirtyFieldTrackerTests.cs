using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class EventDirtyFieldTrackerTests
{
    [Fact]
    public void Merge_DoesNotMarkSameInstantWithDifferentOffsetAsStartEnd()
    {
        var original = CreateTimedEvent(TimeSpan.FromHours(9));
        var candidate = CreateTimedEvent(TimeSpan.Zero);
        candidate.Start = original.Start.ToOffset(TimeSpan.Zero);
        candidate.End = original.End.ToOffset(TimeSpan.Zero);
        candidate.Description = "after";

        Assert.Equal("Description", EventDirtyFieldTracker.Merge(null, original, candidate));
    }

    [Fact]
    public void Merge_MarksActualTimedChangeAsStartEnd()
    {
        var original = CreateTimedEvent(TimeSpan.Zero);
        var candidate = CreateTimedEvent(TimeSpan.Zero);
        candidate.Start = candidate.Start.AddMinutes(30);

        Assert.Equal("StartEnd", EventDirtyFieldTracker.Merge(null, original, candidate));
    }

    [Fact]
    public void Merge_UsesDatesForAllDayEvents()
    {
        var original = CreateTimedEvent(TimeSpan.FromHours(9));
        original.IsAllDay = true;
        var sameDates = CreateTimedEvent(TimeSpan.Zero);
        sameDates.IsAllDay = true;
        sameDates.Start = new DateTimeOffset(original.Start.Date, TimeSpan.Zero);
        sameDates.End = new DateTimeOffset(original.End.Date, TimeSpan.Zero);
        var changedDate = CreateTimedEvent(TimeSpan.FromHours(9));
        changedDate.IsAllDay = true;
        changedDate.End = changedDate.End.AddDays(1);

        Assert.Equal(string.Empty, EventDirtyFieldTracker.Merge(null, original, sameDates));
        Assert.Equal("StartEnd", EventDirtyFieldTracker.Merge(null, original, changedDate));
    }

    private static CalendarEvent CreateTimedEvent(TimeSpan offset) => new()
    {
        Title = "event",
        Description = "before",
        Start = new DateTimeOffset(2026, 6, 10, 9, 0, 0, offset),
        End = new DateTimeOffset(2026, 6, 10, 10, 0, 0, offset)
    };
}
