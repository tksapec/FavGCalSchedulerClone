using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class RecurrenceSplitRegressionTests
{
    [Fact]
    public void BuildSplitSourceRecurrenceJson_PreservesUnsupportedRulePartsAndOnlyAdjustsCount()
    {
        var master = CreateMaster("[\"RRULE:FREQ=MONTHLY;BYDAY=MO;BYSETPOS=1;WKST=MO;COUNT=6\"]");
        var splitStart = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

        var result = RecurrenceRuleHelper.BuildSplitSourceRecurrenceJson(master, splitStart);

        Assert.Contains("BYDAY=MO", result);
        Assert.Contains("BYSETPOS=1", result);
        Assert.Contains("WKST=MO", result);
        Assert.Contains("COUNT=2", result);
        Assert.DoesNotContain("COUNT=6", result);
    }

    [Fact]
    public void BuildSplitFutureRecurrenceJson_PreservesOrdinalByDayAndOtherRuleParts()
    {
        var master = CreateMaster("[\"RRULE:FREQ=MONTHLY;BYDAY=-1MO;BYSETPOS=-1;WKST=MO;COUNT=6\"]", day: 26);
        var splitStart = new DateTimeOffset(2026, 3, 30, 9, 0, 0, TimeSpan.Zero);

        var result = RecurrenceRuleHelper.BuildSplitFutureRecurrenceJson(master, splitStart);

        Assert.Contains("BYDAY=-1MO", result);
        Assert.Contains("BYSETPOS=-1", result);
        Assert.Contains("WKST=MO", result);
        Assert.Contains("COUNT=4", result);
    }

    private static CalendarEvent CreateMaster(string recurrenceJson, int day = 5) => new()
    {
        Id = "split-master",
        CalendarId = "primary",
        Title = "Split series",
        Start = new DateTimeOffset(2026, 1, day, 9, 0, 0, TimeSpan.Zero),
        End = new DateTimeOffset(2026, 1, day, 10, 0, 0, TimeSpan.Zero),
        RecurrenceJson = recurrenceJson
    };
}
