using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class RecurrenceSetRegressionTests
{
    [Fact]
    public void ExpandForRange_RRuleAndRDate_IncludesAdditionalRDateOccurrence()
    {
        var master = CreateMaster(
            "rrule-rdate",
            "[\"RRULE:FREQ=DAILY;COUNT=1\",\"RDATE:20260512T090000Z\"]");

        var results = Expand(master, new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            new[] { new DateTime(2026, 5, 10), new DateTime(2026, 5, 12) },
            results.Select(item => item.Start.Date).Distinct().ToArray());
    }

    [Fact]
    public void ExpandForRange_MultipleRRules_UsesUnionWithoutDuplicates()
    {
        var master = CreateMaster(
            "multiple-rrules",
            "[\"RRULE:FREQ=DAILY;COUNT=1\",\"RRULE:FREQ=WEEKLY;COUNT=2\"]");

        var results = Expand(master, new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            new[] { new DateTime(2026, 5, 10), new DateTime(2026, 5, 17) },
            results.Select(item => item.Start.Date).ToArray());
    }

    [Fact]
    public void ExpandForRange_CombinedSet_AppliesExDateAfterUnion()
    {
        var master = CreateMaster(
            "combined-exdate",
            "[\"RRULE:FREQ=DAILY;COUNT=2\",\"RDATE:20260512T090000Z\",\"EXDATE:20260511T090000Z\"]");

        var results = Expand(master, new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            new[] { new DateTime(2026, 5, 10), new DateTime(2026, 5, 12) },
            results.Select(item => item.Start.Date).ToArray());
    }

    [Fact]
    public void ExpandForRange_ExRule_RemovesMatchingOccurrences()
    {
        var master = CreateMaster(
            "combined-exrule",
            "[\"RRULE:FREQ=DAILY;COUNT=4\",\"EXRULE:FREQ=DAILY;INTERVAL=2;COUNT=2\"]");

        var results = Expand(master, new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            new[] { new DateTime(2026, 5, 11), new DateTime(2026, 5, 13) },
            results.Select(item => item.Start.Date).ToArray());
    }

    [Fact]
    public void ExpandForRange_RDateOnlyMaster_DoesNotDisappear()
    {
        var master = CreateMaster(
            "rdate-only",
            "[\"RDATE:20260512T090000Z\"]");

        var results = Expand(master, new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains(results, item => item.Start == new DateTimeOffset(2026, 5, 12, 9, 0, 0, TimeSpan.Zero));
    }

    private static IReadOnlyList<CalendarEvent> Expand(CalendarEvent master, DateTimeOffset start, DateTimeOffset end)
    {
        return RecurrenceExpansionService.ExpandForRange([master], start, end);
    }

    private static CalendarEvent CreateMaster(string id, string recurrenceJson)
    {
        return new CalendarEvent
        {
            Id = id,
            Title = id,
            Start = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 10, 10, 0, 0, TimeSpan.Zero),
            RecurrenceJson = recurrenceJson
        };
    }
}
