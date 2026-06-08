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

    [Fact]
    public async Task CheckDueRemindersAsync_DoesNotMarkFiredWhenNotificationDispatchFails()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        var attempts = 0;
        service.ReminderTriggered += _ =>
        {
            attempts++;
            throw new InvalidOperationException("notification failed");
        };

        var start = new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.FromHours(9));
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Retry reminder",
            CalendarId = "primary",
            Start = start,
            End = start.AddHours(1),
            ReminderMinutesBeforeStart = 0
        });

        await service.CheckDueRemindersAsync(start);
        await service.CheckDueRemindersAsync(start.AddMinutes(1));

        Assert.Equal(2, attempts);
        var history = await service.LoadHistoryAsync();
        var item = Assert.Single(history);
        Assert.False(item.DeliverySucceeded);
        Assert.Equal(2, item.FailureCount);
        Assert.NotNull(item.LastFailedAt);
    }

    [Fact]
    public async Task CheckDueRemindersAsync_AddsNewFailureHistoryAfterAggregationWindow()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        service.ReminderTriggered += _ => throw new InvalidOperationException("notification failed");

        var start = new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.FromHours(9));
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Retry reminder after window",
            CalendarId = "primary",
            Start = start,
            End = start.AddHours(1),
            ReminderMinutesBeforeStart = 0
        });

        await service.CheckDueRemindersAsync(start);
        await service.CheckDueRemindersAsync(start.AddMinutes(6));

        var history = await service.LoadHistoryAsync();
        Assert.Equal(2, history.Count);
        Assert.All(history, item => Assert.False(item.DeliverySucceeded));
        Assert.All(history, item => Assert.Equal(1, item.FailureCount));
    }

    [Fact]
    public async Task ShowTestNotificationAsync_RecordsFailureWhenNoNotifierExists()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);

        var success = await service.ShowTestNotificationAsync();

        Assert.False(success);
        var item = Assert.Single(await service.LoadHistoryAsync());
        Assert.False(item.DeliverySucceeded);
        Assert.Contains("No reminder notifier", item.DeliveryError);
    }

    [Fact]
    public async Task ShowTestNotificationAsync_UsesSuppliedNotifierBeforeSettingsAreSaved()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        var notifier = new RecordingNotifier();

        var success = await service.ShowTestNotificationAsync(notifier);

        Assert.True(success);
        Assert.Equal(1, notifier.Count);
        var item = Assert.Single(await service.LoadHistoryAsync());
        Assert.True(item.DeliverySucceeded);
        Assert.Equal(nameof(RecordingNotifier), item.DeliveryMethod);
    }

    [Fact]
    public async Task ShowTestNotificationAsync_StoresNotifierMetadataInHistory()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        var notifier = new MetadataNotifier();

        var success = await service.ShowTestNotificationAsync(notifier);

        Assert.True(success);
        var item = Assert.Single(await service.LoadHistoryAsync());
        Assert.Equal("Toast + MessageBox", item.DeliveryMethod);
        Assert.True(item.UsedMessageBoxFallback);
        Assert.Equal(MessageBoxNotificationRole.Fallback, item.MessageBoxRole);
        Assert.True(item.ToastVerified);
        Assert.Equal("Ready", item.ToastStatus);
        Assert.Equal(ReminderSoundStatus.Played, item.SoundStatus);
        Assert.Equal("played", item.SoundError);
        Assert.Contains("verified", item.DeliveryStatusText);
    }

    [Fact]
    public async Task ShowTestNotificationDetailedAsync_ReturnsDispatchMetadata()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        var notifier = new MetadataNotifier();

        var result = await service.ShowTestNotificationDetailedAsync(notifier);

        Assert.True(result.Succeeded);
        Assert.Equal("Toast + MessageBox", result.DeliveryMethod);
        Assert.True(result.UsedMessageBoxFallback);
        Assert.Equal(MessageBoxNotificationRole.Fallback, result.MessageBoxRole);
        Assert.True(result.ToastVerified);
        Assert.Equal("Ready", result.ToastStatus);
        Assert.Equal(ReminderSoundStatus.Played, result.SoundStatus);
        Assert.Equal("played", result.SoundError);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ShowTestNotificationDetailedAsync_TreatsMessageBoxPrimaryAsNotFallback()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        var notifier = new PrimaryMessageBoxMetadataNotifier();

        var result = await service.ShowTestNotificationDetailedAsync(notifier);

        Assert.True(result.Succeeded);
        Assert.False(result.UsedMessageBoxFallback);
        Assert.Equal(MessageBoxNotificationRole.Primary, result.MessageBoxRole);
        var item = Assert.Single(await service.LoadHistoryAsync());
        Assert.Equal("MessageBox通知", item.MessageBoxRoleText);
    }

    [Fact]
    public async Task FallbackReminderNotifier_MarksMessageBoxFallbackOnlyWhenPrimaryFails()
    {
        var primary = new ThrowingMetadataNotifier();
        var fallback = new PrimaryMessageBoxMetadataNotifier();
        var notifier = new FallbackReminderNotifier(primary, fallback);

        await notifier.ShowAsync(CreateTestNotification());

        Assert.True(notifier.UsedMessageBoxFallback);
        Assert.Equal(MessageBoxNotificationRole.Fallback, notifier.MessageBoxRole);
        Assert.Contains("failed ->", notifier.DeliveryMethodName);
    }

    [Fact]
    public async Task FallbackReminderNotifier_MarksMessageBoxAfterToastWhenAlwaysShownAfterPrimarySuccess()
    {
        var primary = new MetadataNotifier();
        var fallback = new PrimaryMessageBoxMetadataNotifier();
        var notifier = new FallbackReminderNotifier(primary, fallback, alwaysShowFallback: true);

        await notifier.ShowAsync(CreateTestNotification());

        Assert.False(notifier.UsedMessageBoxFallback);
        Assert.Equal(MessageBoxNotificationRole.AfterToast, notifier.MessageBoxRole);
        Assert.Contains("+", notifier.DeliveryMethodName);
    }

    [Fact]
    public async Task CustomPopupReminderNotifier_ReportsPopupDeliveryWithoutMessageBoxFallback()
    {
        var displayed = false;
        var notifier = new CustomPopupReminderNotifier(
            (_, _) => Task.CompletedTask,
            (_, _, _, _) =>
            {
                displayed = true;
                return Task.CompletedTask;
            });

        await notifier.ShowAsync(CreateTestNotification());

        Assert.True(displayed);
        Assert.Equal("CustomPopup", notifier.DeliveryMethodName);
        Assert.False(notifier.UsedMessageBoxFallback);
        Assert.Equal(MessageBoxNotificationRole.None, notifier.MessageBoxRole);
    }

    [Fact]
    public async Task ShowTestNotificationDetailedAsync_RecordsMissingSoundFileWithoutFailingNotification()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        var inner = new RecordingNotifier();
        var notifier = new SoundReminderNotifier(inner, "C:\\missing\\notify.wav", 80, _ => false, (_, _) => throw new InvalidOperationException("should not play"));

        var result = await service.ShowTestNotificationDetailedAsync(notifier);

        Assert.True(result.Succeeded);
        Assert.Equal(ReminderSoundStatus.MissingFile, result.SoundStatus);
        Assert.Contains("notify.wav", result.SoundError);
        var item = Assert.Single(await service.LoadHistoryAsync());
        Assert.Contains("ファイルなし", item.SoundStatusText);
    }

    [Fact]
    public async Task ShowTestNotificationDetailedAsync_RecordsSoundPlaybackFailureWithoutFailingNotification()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ReminderNotificationService(repository);
        var inner = new RecordingNotifier();
        var notifier = new SoundReminderNotifier(inner, "C:\\sound\\notify.wav", 80, _ => true, (_, _) => throw new InvalidOperationException("audio failed"));

        var result = await service.ShowTestNotificationDetailedAsync(notifier);

        Assert.True(result.Succeeded);
        Assert.Equal(ReminderSoundStatus.Failed, result.SoundStatus);
        Assert.Equal("audio failed", result.SoundError);
        var item = Assert.Single(await service.LoadHistoryAsync());
        Assert.Contains("再生失敗", item.SoundStatusText);
    }

    private static async Task<CalendarRepository> CreateRepositoryAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        return repository;
    }

    private static ReminderNotification CreateTestNotification()
    {
        var now = DateTimeOffset.Now;
        return new ReminderNotification("test:key", "test", "Test", "today", now, now, now, GoogleCalendarDefaults.PrimaryCalendarId, false);
    }

    private sealed class RecordingNotifier : IReminderNotifier
    {
        public int Count { get; private set; }

        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class MetadataNotifier : IReminderNotifier, IReminderNotifierMetadata
    {
        public string DeliveryMethodName => "Toast + MessageBox";
        public bool UsedMessageBoxFallback => true;
        public MessageBoxNotificationRole MessageBoxRole => MessageBoxNotificationRole.Fallback;
        public bool ToastVerified => true;
        public string? ToastStatus => "Ready";
        public ReminderSoundStatus SoundStatus => ReminderSoundStatus.Played;
        public string? SoundError => "played";

        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class PrimaryMessageBoxMetadataNotifier : IReminderNotifier, IReminderNotifierMetadata
    {
        public string DeliveryMethodName => "MessageBox";
        public bool UsedMessageBoxFallback => false;
        public MessageBoxNotificationRole MessageBoxRole => MessageBoxNotificationRole.Primary;
        public bool ToastVerified => false;
        public string? ToastStatus => null;
        public ReminderSoundStatus SoundStatus => ReminderSoundStatus.NotConfigured;
        public string? SoundError => null;

        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingMetadataNotifier : IReminderNotifier, IReminderNotifierMetadata
    {
        public string DeliveryMethodName => "WindowsToast";
        public bool UsedMessageBoxFallback => false;
        public MessageBoxNotificationRole MessageBoxRole => MessageBoxNotificationRole.None;
        public bool ToastVerified => false;
        public string? ToastStatus => "トースト通知未確認";
        public ReminderSoundStatus SoundStatus => ReminderSoundStatus.NotConfigured;
        public string? SoundError => null;

        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("toast failed");
        }
    }
}
