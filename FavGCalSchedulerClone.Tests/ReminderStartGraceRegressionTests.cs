using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReminderStartGraceRegressionTests
{
    [Fact]
    public void ZeroMinuteReminder_AllowsOnePollingIntervalAfterStart()
    {
        var start = DateTimeOffset.Parse("2026-11-22T10:00:00+09:00");
        var item = new CalendarEvent { Start = start, End = start.AddHours(1), IsAllDay = false };

        Assert.False(ReminderNotificationService.IsPastForReminder(item, start.AddSeconds(59), 0));
        Assert.True(ReminderNotificationService.IsPastForReminder(item, start.AddSeconds(61), 0));
        Assert.True(ReminderNotificationService.IsPastForReminder(item, start.AddSeconds(1), 5));
    }
}
