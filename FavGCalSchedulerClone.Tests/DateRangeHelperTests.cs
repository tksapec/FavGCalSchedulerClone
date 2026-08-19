using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class DateRangeHelperTests
{
    [Fact]
    public void OccursOn_TreatsAllDayEndAsExclusive()
    {
        var item = new CalendarEvent
        {
            IsAllDay = true,
            Start = new DateTimeOffset(new DateTime(2026, 5, 16)),
            End = new DateTimeOffset(new DateTime(2026, 5, 17))
        };

        Assert.True(DateRangeHelper.OccursOn(item, new DateTime(2026, 5, 16)));
        Assert.False(DateRangeHelper.OccursOn(item, new DateTime(2026, 5, 17)));
    }

    [Fact]
    public void MonthGridRange_ReturnsSixWeeksStartingOnSunday()
    {
        var (start, end) = DateRangeHelper.MonthGridRange(new DateTime(2026, 5, 1));

        Assert.Equal(DayOfWeek.Sunday, start.DayOfWeek);
        Assert.Equal(42, (end - start).TotalDays);
    }

    [Fact]
    public void MonthGridRange_CanStartOnMonday()
    {
        var (start, end) = DateRangeHelper.MonthGridRange(new DateTime(2026, 5, 1), weekStartsOnMonday: true);

        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
        Assert.Equal(new DateTime(2026, 4, 27), start);
        Assert.Equal(42, (end - start).TotalDays);
    }
}
