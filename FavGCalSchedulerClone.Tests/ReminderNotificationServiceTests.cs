using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReminderNotificationServiceTests
{
    [Fact]
    public async Task CheckDueRemindersAsync_TriggersForTimedEvent()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        var notifications = new List<ReminderNotification>();
        service.ReminderTriggered += notification =>
        {
            notifications.Add(notification);
            return Task.CompletedTask;
        };

        var start = new DateTimeOffset(2026, 5, 17, 10, 0, 0, TimeSpan.FromHours(9));
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Design review",
            CalendarId = "primary",
            Start = start,
            End = start.AddHours(1),
            ReminderMinutesBeforeStart = 10
        });

        await service.CheckDueRemindersAsync(start.AddMinutes(-10));

        var notification = Assert.Single(notifications);
        Assert.Equal("Design review", notification.Title);
        Assert.Equal(start.AddMinutes(-10), notification.RemindAt);
    }

    [Fact]
    public async Task CheckDueRemindersAsync_DoesNotRepeatSameOccurrence()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        var count = 0;
        service.ReminderTriggered += _ =>
        {
            count++;
            return Task.CompletedTask;
        };

        var start = new DateTimeOffset(2026, 5, 17, 14, 0, 0, TimeSpan.FromHours(9));
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Standup",
            CalendarId = "primary",
            Start = start,
            End = start.AddMinutes(30),
            ReminderMinutesBeforeStart = 5
        });

        var now = start.AddMinutes(-5);
        await service.CheckDueRemindersAsync(now);
        await service.CheckDueRemindersAsync(now.AddMinutes(1));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CheckDueRemindersAsync_RespectsSnoozeUntilDue()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        var notifications = new List<ReminderNotification>();
        service.ReminderTriggered += notification =>
        {
            notifications.Add(notification);
            return Task.CompletedTask;
        };

        var start = new DateTimeOffset(2026, 5, 17, 15, 0, 0, TimeSpan.FromHours(9));
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Snoozed event",
            CalendarId = "primary",
            Start = start,
            End = start.AddMinutes(30),
            ReminderMinutesBeforeStart = 5
        });

        var dueAt = start.AddMinutes(-5);
        await service.CheckDueRemindersAsync(dueAt);
        var first = Assert.Single(notifications);
        await service.SnoozeAsync(first.OccurrenceKey, 5, dueAt);
        notifications.Clear();

        await service.CheckDueRemindersAsync(dueAt.AddMinutes(4));
        Assert.Empty(notifications);

        await service.CheckDueRemindersAsync(dueAt.AddMinutes(5));
        Assert.Single(notifications);
    }

    [Fact]
    public async Task CheckDueRemindersAsync_KeepsMostRecentFiftyHistoryItems()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        var start = new DateTimeOffset(2026, 5, 17, 16, 0, 0, TimeSpan.FromHours(9));

        for (var index = 0; index < 55; index++)
        {
            await repository.SaveEventAsync(new CalendarEvent
            {
                Id = $"event-{index}",
                Title = $"Event {index}",
                CalendarId = "primary",
                Start = start,
                End = start.AddMinutes(30),
                ReminderMinutesBeforeStart = 0
            });
        }

        await service.CheckDueRemindersAsync(start);

        var history = await service.LoadHistoryAsync();
        Assert.Equal(50, history.Count);
        Assert.DoesNotContain(history, item => item.EventId == "event-0");
    }

    [Fact]
    public async Task CheckDueRemindersAsync_UsesMorningReminderForAllDayEvent()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        var notifications = new List<ReminderNotification>();
        service.ReminderTriggered += notification =>
        {
            notifications.Add(notification);
            return Task.CompletedTask;
        };

        var day = new DateTime(2026, 5, 17);
        var offset = TimeZoneInfo.Local.GetUtcOffset(day);
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Holiday",
            CalendarId = "primary",
            Start = new DateTimeOffset(day, offset),
            End = new DateTimeOffset(day.AddDays(1), offset),
            IsAllDay = true,
            ReminderMinutesBeforeStart = 60
        });

        var now = new DateTimeOffset(day.AddHours(8), offset);
        await service.CheckDueRemindersAsync(now);

        var notification = Assert.Single(notifications);
        Assert.Equal(now, notification.RemindAt);
    }

    [Fact]
    public async Task LoadHistoryAsync_ReturnsEmptyWhenStoredJsonIsCorrupt()
    {
        var repository = await CreateRepositoryAsync();
        await repository.SaveSettingValueAsync("reminder:history", "{not-json");
        var service = new ReminderNotificationService(repository);

        var history = await service.LoadHistoryAsync();

        Assert.Empty(history);
    }

    [Fact]
    public async Task CheckDueRemindersAsync_RecoversFromCorruptStateJson()
    {
        var repository = await CreateRepositoryAsync();
        await repository.SaveSettingValueAsync("reminder:fired", "{not-json");
        await repository.SaveSettingValueAsync("reminder:snoozed", "{not-json");
        var service = new ReminderNotificationService(repository);
        var notifications = new List<ReminderNotification>();
        service.ReminderTriggered += notification =>
        {
            notifications.Add(notification);
            return Task.CompletedTask;
        };

        var start = new DateTimeOffset(2026, 5, 17, 10, 0, 0, TimeSpan.FromHours(9));
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Recovered",
            CalendarId = "primary",
            Start = start,
            End = start.AddHours(1),
            ReminderMinutesBeforeStart = 0
        });

        await service.CheckDueRemindersAsync(start);

        Assert.Single(notifications);
    }

    [Fact]
    public async Task CheckDueRemindersAsync_DoesNotThrowWhenNotificationDispatchFails()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        service.ReminderTriggered += _ => throw new InvalidOperationException("notification failed");

        var start = new DateTimeOffset(2026, 5, 17, 11, 0, 0, TimeSpan.FromHours(9));
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Throwing handler",
            CalendarId = "primary",
            Start = start,
            End = start.AddHours(1),
            ReminderMinutesBeforeStart = 0
        });

        await service.CheckDueRemindersAsync(start);
    }

    private static async Task<CalendarRepository> CreateRepositoryAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        return repository;
    }
}
