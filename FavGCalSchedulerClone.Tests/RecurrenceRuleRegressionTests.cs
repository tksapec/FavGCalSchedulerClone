using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class RecurrenceRuleRegressionTests
{
    [Fact]
    public void ExpandForRange_NegativeByMonthDayMeansLastDayOfMonth()
    {
        var master = CreateMaster(
            "last-day",
            new DateTimeOffset(2026, 1, 31, 9, 0, 0, TimeSpan.Zero),
            "[\"RRULE:FREQ=MONTHLY;BYMONTHDAY=-1;COUNT=3\"]");

        var actual = ExpandDates(master, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            [new DateTime(2026, 1, 31), new DateTime(2026, 2, 28), new DateTime(2026, 3, 31)],
            actual);
    }

    [Fact]
    public void ExpandForRange_InvalidMonthDayIsSkippedInsteadOfClamped()
    {
        var master = CreateMaster(
            "day-30",
            new DateTimeOffset(2026, 1, 30, 9, 0, 0, TimeSpan.Zero),
            "[\"RRULE:FREQ=MONTHLY;BYMONTHDAY=30;COUNT=3\"]");

        var actual = ExpandDates(master, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            [new DateTime(2026, 1, 30), new DateTime(2026, 3, 30), new DateTime(2026, 4, 30)],
            actual);
    }

    [Fact]
    public void ExpandForRange_MonthlyByDayEnumeratesMatchingWeekdays()
    {
        var master = CreateMaster(
            "monthly-tuesday",
            new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero),
            "[\"RRULE:FREQ=MONTHLY;BYDAY=TU;COUNT=3\"]");

        var actual = ExpandDates(master, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            [new DateTime(2026, 1, 6), new DateTime(2026, 1, 13), new DateTime(2026, 1, 20)],
            actual);
    }

    [Theory]
    [InlineData("1MO", 5, 2, 2)]
    [InlineData("-1MO", 26, 23, 30)]
    public void ExpandForRange_OrdinalMonthlyByDayPreservesOrdinal(
        string byDay,
        int januaryDay,
        int februaryDay,
        int marchDay)
    {
        var master = CreateMaster(
            $"ordinal-{byDay}",
            new DateTimeOffset(2026, 1, januaryDay, 9, 0, 0, TimeSpan.Zero),
            $"[\"RRULE:FREQ=MONTHLY;BYDAY={byDay};COUNT=3\"]");

        var actual = ExpandDates(master, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            [new DateTime(2026, 1, januaryDay), new DateTime(2026, 2, februaryDay), new DateTime(2026, 3, marchDay)],
            actual);
    }

    private static DateTime[] ExpandDates(CalendarEvent master, DateTimeOffset start, DateTimeOffset end) =>
        RecurrenceExpansionService.ExpandForRange([master], start, end)
            .Select(item => item.Start.Date)
            .ToArray();

    private static CalendarEvent CreateMaster(string id, DateTimeOffset start, string recurrenceJson) => new()
    {
        Id = id,
        CalendarId = "primary",
        Title = id,
        Start = start,
        End = start.AddHours(1),
        RecurrenceJson = recurrenceJson
    };
}
