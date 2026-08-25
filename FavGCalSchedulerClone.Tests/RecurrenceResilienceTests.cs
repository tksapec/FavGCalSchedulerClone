using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class RecurrenceResilienceTests
{
    [Fact]
    public void ExpandForRange_SkipsMalformedRecurringMasterWithoutLosingOtherEvents()
    {
        var rangeStart = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(9));
        var rangeEnd = rangeStart.AddMonths(1);
        var malformed = new CalendarEvent
        {
            Id = "bad-series",
            CalendarId = "primary",
            Title = "Broken recurrence",
            Start = rangeStart.AddDays(1).AddHours(9),
            End = rangeStart.AddDays(1).AddHours(10),
            RecurrenceJson = JsonSerializer.Serialize(new[] { "RRULE:FREQ=NOT_SUPPORTED" })
        };
        var normal = new CalendarEvent
        {
            Id = "normal",
            CalendarId = "primary",
            Title = "Normal event",
            Start = rangeStart.AddDays(2).AddHours(9),
            End = rangeStart.AddDays(2).AddHours(10)
        };

        IReadOnlyList<CalendarEvent>? expanded = null;
        var exception = Record.Exception(() =>
            expanded = RecurrenceExpansionService.ExpandForRange([malformed, normal], rangeStart, rangeEnd));

        Assert.Null(exception);
        Assert.NotNull(expanded);
        Assert.Contains(expanded, item => item.Id == normal.Id);
        Assert.DoesNotContain(expanded, item => item.Id.StartsWith("bad-series@", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandForRange_IgnoresNullRecurrenceEntriesAndUsesValidRule()
    {
        var rangeStart = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(9));
        var masterStart = rangeStart.AddDays(1).AddHours(9);
        var master = new CalendarEvent
        {
            Id = "series",
            CalendarId = "primary",
            Title = "Recurring event",
            Start = masterStart,
            End = masterStart.AddHours(1),
            RecurrenceJson = "[null,\"RRULE:FREQ=DAILY;COUNT=2\"]"
        };

        var expanded = RecurrenceExpansionService.ExpandForRange([master], rangeStart, rangeStart.AddMonths(1));

        Assert.Equal(2, expanded.Count(item => item.Id.StartsWith("series@", StringComparison.Ordinal)));
    }

    [Fact]
    public void ExpandForRange_NormalizesNullReminderCollectionsWhileCloning()
    {
        var rangeStart = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(9));
        var calendarEvent = new CalendarEvent
        {
            Id = "event",
            CalendarId = "primary",
            Title = "Event",
            Start = rangeStart.AddDays(1).AddHours(9),
            End = rangeStart.AddDays(1).AddHours(10),
            AppReminderMinutesBeforeStart = null!,
            GoogleEmailReminderMinutesBeforeStart = null!
        };

        IReadOnlyList<CalendarEvent>? expanded = null;
        var exception = Record.Exception(() =>
            expanded = RecurrenceExpansionService.ExpandForRange([calendarEvent], rangeStart, rangeStart.AddMonths(1)));

        Assert.Null(exception);
        var clone = Assert.Single(expanded!);
        Assert.Empty(clone.AppReminderMinutesBeforeStart);
        Assert.Empty(clone.GoogleEmailReminderMinutesBeforeStart);
    }
}
