using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class RecurrenceExpansionServiceTests
{
    [Fact]
    public void ExpandForRange_ExpandsRecurringMasterIntoVisibleOccurrences()
    {
        var master = new CalendarEvent
        {
            Id = "series-1",
            Title = "Daily standup",
            Start = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 10, 9, 30, 0, TimeSpan.Zero),
            RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=3\"]"
        };

        var results = RecurrenceExpansionService.ExpandForRange(
            [master],
            new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(3, results.Count);
        Assert.All(results, item => Assert.True(item.IsGeneratedOccurrence));
        Assert.Equal(
            [new DateTime(2026, 5, 10), new DateTime(2026, 5, 11), new DateTime(2026, 5, 12)],
            results.Select(item => item.Start.Date).ToArray());
    }

    [Fact]
    public void ExpandForRange_ReplacesOccurrenceWithEditedException()
    {
        var master = new CalendarEvent
        {
            Id = "series-1",
            GoogleEventId = "remote-series",
            Title = "Daily standup",
            Start = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 10, 9, 30, 0, TimeSpan.Zero),
            RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=3\"]"
        };
        var exception = new CalendarEvent
        {
            Id = "exception-1",
            GoogleEventId = "remote-instance",
            RecurringEventId = "remote-series",
            RecurringParentId = "series-1",
            OriginalStart = new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero),
            IsRecurrenceException = true,
            Title = "Moved standup",
            Start = new DateTimeOffset(2026, 5, 11, 15, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 11, 15, 30, 0, TimeSpan.Zero)
        };

        var results = RecurrenceExpansionService.ExpandForRange(
            [master, exception],
            new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(3, results.Count);
        Assert.Contains(results, item => item.Title == "Moved standup" && item.Start.Hour == 15);
        Assert.DoesNotContain(results, item => item.Title == "Daily standup" && item.Start.Date == new DateTime(2026, 5, 11) && item.Start.Hour == 9);
    }

    [Fact]
    public void ExpandForRange_ReplacesGoogleExceptionMatchedByRemoteSeriesIdOnly()
    {
        var master = new CalendarEvent
        {
            Id = "g:primary:remote-series",
            GoogleEventId = "remote-series",
            Title = "Daily standup",
            Start = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 10, 9, 30, 0, TimeSpan.Zero),
            RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=3\"]"
        };
        var exception = new CalendarEvent
        {
            Id = "g:primary:remote-instance",
            GoogleEventId = "remote-instance",
            RecurringEventId = "remote-series",
            OriginalStart = new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero),
            IsRecurrenceException = true,
            Title = "Moved standup",
            Start = new DateTimeOffset(2026, 5, 11, 15, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 11, 15, 30, 0, TimeSpan.Zero)
        };

        var results = RecurrenceExpansionService.ExpandForRange(
            [master, exception],
            new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(3, results.Count);
        Assert.Single(results, item => item.Start.Date == new DateTime(2026, 5, 11));
        Assert.Contains(results, item => item.Title == "Moved standup" && item.Start.Hour == 15);
        Assert.DoesNotContain(results, item => item.Title == "Daily standup" && item.Start.Date == new DateTime(2026, 5, 11) && item.Start.Hour == 9);
    }

    [Fact]
    public void ExpandForRange_SuppressesDeletedOccurrenceException()
    {
        var master = new CalendarEvent
        {
            Id = "series-1",
            GoogleEventId = "remote-series",
            Title = "Workout",
            Start = new DateTimeOffset(2026, 5, 10, 6, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 10, 7, 0, 0, TimeSpan.Zero),
            RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=3\"]"
        };
        var deletedException = new CalendarEvent
        {
            Id = "exception-2",
            RecurringEventId = "remote-series",
            RecurringParentId = "series-1",
            OriginalStart = new DateTimeOffset(2026, 5, 11, 6, 0, 0, TimeSpan.Zero),
            IsRecurrenceException = true,
            IsDeleted = true,
            Title = "Workout",
            Start = new DateTimeOffset(2026, 5, 11, 6, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 11, 7, 0, 0, TimeSpan.Zero)
        };

        var results = RecurrenceExpansionService.ExpandForRange(
            [master, deletedException],
            new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, item => item.Start.Date == new DateTime(2026, 5, 11));
    }

    [Fact]
    public void ExpandForRange_SuppressesGoogleDeletedExceptionMatchedByRemoteSeriesIdOnly()
    {
        var master = new CalendarEvent
        {
            Id = "g:primary:remote-series",
            GoogleEventId = "remote-series",
            Title = "Workout",
            Start = new DateTimeOffset(2026, 5, 10, 6, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 10, 7, 0, 0, TimeSpan.Zero),
            RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=3\"]"
        };
        var deletedException = new CalendarEvent
        {
            Id = "g:primary:remote-instance",
            RecurringEventId = "remote-series",
            OriginalStart = new DateTimeOffset(2026, 5, 11, 6, 0, 0, TimeSpan.Zero),
            IsRecurrenceException = true,
            IsDeleted = true,
            Title = "Workout",
            Start = new DateTimeOffset(2026, 5, 11, 6, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 11, 7, 0, 0, TimeSpan.Zero)
        };

        var results = RecurrenceExpansionService.ExpandForRange(
            [master, deletedException],
            new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, item => item.Start.Date == new DateTime(2026, 5, 11));
    }

    [Fact]
    public void ExpandForRange_ReplacesOccurrenceWithLocalParentOnlyException()
    {
        var master = new CalendarEvent
        {
            Id = "series-1",
            Title = "Daily standup",
            Start = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 10, 9, 30, 0, TimeSpan.Zero),
            RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=3\"]"
        };
        var exception = new CalendarEvent
        {
            Id = "exception-1",
            RecurringParentId = "series-1",
            OriginalStart = new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero),
            IsRecurrenceException = true,
            Title = "Moved standup",
            Start = new DateTimeOffset(2026, 5, 11, 15, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 11, 15, 30, 0, TimeSpan.Zero)
        };

        var results = RecurrenceExpansionService.ExpandForRange(
            [master, exception],
            new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(3, results.Count);
        Assert.Contains(results, item => item.Title == "Moved standup" && item.Start.Hour == 15);
        Assert.DoesNotContain(results, item => item.Title == "Daily standup" && item.Start.Date == new DateTime(2026, 5, 11) && item.Start.Hour == 9);
    }

    [Fact]
    public void ExpandForRange_HonorsTimedExDateOnMaster()
    {
        var master = new CalendarEvent
        {
            Id = "series-1",
            Title = "Daily standup",
            Start = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 10, 9, 30, 0, TimeSpan.Zero),
            RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=3\",\"EXDATE:20260511T090000Z\"]"
        };

        var results = RecurrenceExpansionService.ExpandForRange(
            [master],
            new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, item => item.Start.Date == new DateTime(2026, 5, 11));
    }

    [Fact]
    public void ExpandForRange_DailyCountIsSeriesTotalBeforeRangeFilter()
    {
        var master = RecurringMaster("daily", new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), "[\"RRULE:FREQ=DAILY;COUNT=3\"]");

        var results = RecurrenceExpansionService.ExpandForRange(
            [master],
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(results);
    }

    [Fact]
    public void ExpandForRange_WeeklyCountStopsBeforeThirdOccurrence()
    {
        var master = RecurringMaster("weekly", new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), "[\"RRULE:FREQ=WEEKLY;COUNT=2\"]");

        var results = RecurrenceExpansionService.ExpandForRange(
            [master],
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal([new DateTime(2026, 1, 1), new DateTime(2026, 1, 8)], results.Select(item => item.Start.Date).ToArray());
    }

    [Fact]
    public void ExpandForRange_ByDayCountIsSeriesTotalBeforeRangeFilter()
    {
        var master = RecurringMaster("byday", new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), "[\"RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=2\"]");

        var results = RecurrenceExpansionService.ExpandForRange(
            [master],
            new DateTimeOffset(2026, 1, 12, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(results);
    }

    [Fact]
    public void ExpandForRange_ByMonthDayCountIsSeriesTotalBeforeRangeFilter()
    {
        var master = RecurringMaster("bymonthday", new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), "[\"RRULE:FREQ=MONTHLY;BYMONTHDAY=1,15;COUNT=2\"]");

        var results = RecurrenceExpansionService.ExpandForRange(
            [master],
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(results);
    }

    [Fact]
    public void ExpandForRange_YearlyCountIsSeriesTotalBeforeRangeFilter()
    {
        var master = RecurringMaster("yearly", new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), "[\"RRULE:FREQ=YEARLY;COUNT=1\"]");

        var results = RecurrenceExpansionService.ExpandForRange(
            [master],
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(results);
    }

    private static CalendarEvent RecurringMaster(string id, DateTimeOffset start, string recurrenceJson)
    {
        return new CalendarEvent
        {
            Id = id,
            Title = id,
            Start = start,
            End = start.AddHours(1),
            RecurrenceJson = recurrenceJson
        };
    }
}
