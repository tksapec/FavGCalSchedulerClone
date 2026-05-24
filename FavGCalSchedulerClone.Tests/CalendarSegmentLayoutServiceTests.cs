using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarSegmentLayoutServiceTests
{
    [Fact]
    public void PopulateSegments_RendersSingleDayEventAsOneCompleteSegment()
    {
        var days = CreateDays(new DateTime(2026, 5, 10), 7);
        var item = Event("Meeting", new DateTime(2026, 5, 11), new DateTime(2026, 5, 12));

        CalendarSegmentLayoutService.PopulateSegments(days, [item]);

        var segment = Assert.Single(days[1].Segments, segment => segment.IsVisible);
        Assert.Same(item, segment.Event);
        Assert.True(segment.IsWeekSegmentStart);
        Assert.True(segment.IsWeekSegmentEnd);
        Assert.True(segment.ShowText);
    }

    [Fact]
    public void PopulateSegments_ConnectsMultiDayEventInSameLane()
    {
        var days = CreateDays(new DateTime(2026, 5, 10), 7);
        var item = Event("NHP来日", new DateTime(2026, 5, 11), new DateTime(2026, 5, 14));

        CalendarSegmentLayoutService.PopulateSegments(days, [item]);

        var segments = days.SelectMany(day => day.Segments).Where(segment => segment.Event == item).ToArray();
        Assert.Equal(3, segments.Length);
        Assert.All(segments, segment => Assert.Equal(0, segment.Lane));
        Assert.True(segments[0].IsWeekSegmentStart);
        Assert.False(segments[1].IsWeekSegmentStart);
        Assert.True(segments[2].IsWeekSegmentEnd);
        Assert.True(segments[0].ShowText);
        Assert.False(segments[1].ShowText);
    }

    [Fact]
    public void PopulateSegments_RepeatsTitleAtStartOfEachWeekRow()
    {
        var days = CreateDays(new DateTime(2026, 5, 10), 14);
        var item = Event("Trip", new DateTime(2026, 5, 15), new DateTime(2026, 5, 19));

        CalendarSegmentLayoutService.PopulateSegments(days, [item]);

        var segments = days.SelectMany(day => day.Segments).Where(segment => segment.Event == item).ToArray();
        Assert.Equal(4, segments.Length);
        Assert.True(segments.Single(segment => segment.Date == new DateTime(2026, 5, 15)).ShowText);
        Assert.True(segments.Single(segment => segment.Date == new DateTime(2026, 5, 17)).ShowText);
    }

    [Fact]
    public void PopulateSegments_ConnectsAcrossMonthBoundaryInVisibleWeek()
    {
        var days = CreateDays(new DateTime(2026, 4, 27), 7);
        var item = Event("Month boundary", new DateTime(2026, 4, 30), new DateTime(2026, 5, 3));

        CalendarSegmentLayoutService.PopulateSegments(days, [item]);

        var dates = days.SelectMany(day => day.Segments)
            .Where(segment => segment.Event == item)
            .Select(segment => segment.Date)
            .ToArray();
        Assert.Equal([new DateTime(2026, 4, 30), new DateTime(2026, 5, 1), new DateTime(2026, 5, 2)], dates);
    }

    [Fact]
    public void PopulateSegments_ConnectsTimedEventThatSpansMidnight()
    {
        var days = CreateDays(new DateTime(2026, 5, 11), 7);
        var item = new CalendarEvent
        {
            Id = "overnight",
            Title = "Overnight",
            Start = new DateTimeOffset(2026, 5, 11, 23, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 5, 13, 1, 0, 0, TimeSpan.FromHours(9))
        };

        CalendarSegmentLayoutService.PopulateSegments(days, [item]);

        var segments = days.SelectMany(day => day.Segments).Where(segment => segment.Event == item).ToArray();
        Assert.Equal(3, segments.Length);
        Assert.Equal("23:00 Overnight", segments[0].DisplayText);
        Assert.True(segments[2].IsWeekSegmentEnd);
    }

    [Fact]
    public void PopulateSegments_ReservesLanesForLongEventsBeforeSingleDayOverflow()
    {
        var days = CreateDays(new DateTime(2026, 5, 10), 7);
        var longEvent = Event("Long", new DateTime(2026, 5, 11), new DateTime(2026, 5, 13));
        var sameDayEvents = Enumerable.Range(0, 5)
            .Select(index => Event($"Short {index}", new DateTime(2026, 5, 11), new DateTime(2026, 5, 12)))
            .ToArray();

        CalendarSegmentLayoutService.PopulateSegments(days, [.. sameDayEvents, longEvent]);

        Assert.Contains(days[1].Segments, segment => segment.Event == longEvent && segment.Lane == 0);
        Assert.Equal(5, days[1].Segments.Count(segment => segment.IsVisible));
        Assert.Equal(4, days[1].Segments.Count(segment => sameDayEvents.Contains(segment.Event)));
    }

    [Fact]
    public void PopulateSegments_UsesExistingColorForTodoSegment()
    {
        var days = CreateDays(new DateTime(2026, 5, 10), 7);
        var todo = Event("Todo", new DateTime(2026, 5, 11), new DateTime(2026, 5, 12));
        todo.IsTodoLike = true;
        todo.Description = "#todoA20%";
        todo.DisplayColor = "#7AE7BF";

        CalendarSegmentLayoutService.PopulateSegments(days, [todo]);

        var segment = Assert.Single(days[1].Segments, segment => segment.IsVisible);
        Assert.Equal("#7AE7BF", segment.DisplayColor);
        Assert.Equal("[ ] [A] 20% Todo", segment.DisplayText);
    }

    private static CalendarDay[] CreateDays(DateTime start, int count) =>
        Enumerable.Range(0, count).Select(offset => new CalendarDay { Date = start.AddDays(offset) }).ToArray();

    private static CalendarEvent Event(string title, DateTime start, DateTime end) => new()
    {
        Id = title,
        Title = title,
        IsAllDay = true,
        Start = new DateTimeOffset(start),
        End = new DateTimeOffset(end)
    };
}
