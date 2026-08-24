using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class NewScheduleTimeDefaultsTests
{
    [Theory]
    [InlineData(2026, 8, 9, 9, 0, 9, 0, 10, 0)]
    [InlineData(2026, 8, 9, 9, 1, 9, 30, 10, 30)]
    [InlineData(2026, 8, 9, 9, 30, 9, 30, 10, 30)]
    public void Create_RoundsToNearestFutureHalfHourAndKeepsOneHourDuration(
        int year, int month, int day, int hour, int minute,
        int expectedStartHour, int expectedStartMinute, int expectedEndHour, int expectedEndMinute)
    {
        var result = NewScheduleTimeDefaults.Create(new DateTime(year, month, day, hour, minute, 0));

        Assert.Equal(new TimeSpan(expectedStartHour, expectedStartMinute, 0), result.Start.TimeOfDay);
        Assert.Equal(new TimeSpan(expectedEndHour, expectedEndMinute, 0), result.End.TimeOfDay);
        Assert.Equal(TimeSpan.FromHours(1), result.End - result.Start);
    }

    [Fact]
    public void Create_RollsOverToTheNextDayAfterTheLastHalfHour()
    {
        var result = NewScheduleTimeDefaults.Create(new DateTime(2026, 8, 9, 23, 31, 0));

        Assert.Equal(new DateTime(2026, 8, 10, 0, 0, 0), result.Start);
        Assert.Equal(new DateTime(2026, 8, 10, 1, 0, 0), result.End);
    }
}
