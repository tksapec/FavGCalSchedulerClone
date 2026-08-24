using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class RecurrenceExDateTimeZoneRegressionTests
{
    [Fact]
    public void ExpandOccurrences_FloatingExDateUsesSeriesTimeZoneAcrossDst()
    {
        var master = new CalendarEvent
        {
            Id = "floating-exdate-dst",
            CalendarId = "work",
            Title = "NY weekly",
            Start = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.FromHours(-5)),
            End = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.FromHours(-5)),
            StartTimeZoneId = "America/New_York",
            EndTimeZoneId = "America/New_York",
            RecurrenceJson = "[\"RRULE:FREQ=WEEKLY;COUNT=2\",\"EXDATE:20260309T090000\"]"
        };

        var occurrences = RecurrenceRuleHelper.ExpandOccurrences(
            master,
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.FromHours(-4)))
            .ToArray();

        var occurrence = Assert.Single(occurrences);
        Assert.Equal(new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.FromHours(-5)), occurrence);
    }
}
