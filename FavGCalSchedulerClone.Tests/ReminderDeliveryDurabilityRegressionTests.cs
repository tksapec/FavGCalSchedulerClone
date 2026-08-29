using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReminderDeliveryDurabilityRegressionTests
{
    [Fact]
    public async Task SuccessfulDelivery_WhenHistoryPersistenceFails_DoesNotNotifyAgain()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reminder-durability-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var now = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.FromHours(9));
            await repository.SaveEventAsync(new CalendarEvent
            {
                Id = "durable-reminder",
                Title = "Durable reminder",
                Start = now.AddMinutes(30),
                End = now.AddHours(1),
                ReminderMinutesBeforeStart = 30,
                IsAppReminderEnabled = true
            });

            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TRIGGER fail_reminder_history_insert
                    BEFORE INSERT ON settings
                    WHEN NEW.key = 'reminder:history'
                    BEGIN
                        SELECT RAISE(ABORT, 'intentional reminder history write failure');
                    END;
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var notifier = new RecordingNotifier();
            using var service = new ReminderNotificationService(repository, notifier);

            await service.CheckDueRemindersAsync(now);
            Assert.Equal(1, notifier.Count);

            await service.CheckDueRemindersAsync(now.AddSeconds(1));

            Assert.Equal(1, notifier.Count);
            var firedJson = await repository.LoadSettingValueAsync("reminder:fired");
            Assert.False(string.IsNullOrWhiteSpace(firedJson));
            Assert.Contains("durable-reminder", firedJson, StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
        }
    }

    [Fact]
    public async Task SnoozedRedelivery_WhenSnoozeRemovalFails_DoesNotNotifyThirdTime()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reminder-snooze-durability-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var now = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.FromHours(9));
            var start = now.AddMinutes(30);
            await repository.SaveEventAsync(new CalendarEvent
            {
                Id = "durable-snooze",
                Title = "Durable snooze",
                Start = start,
                End = start.AddMinutes(30),
                ReminderMinutesBeforeStart = 30,
                IsAppReminderEnabled = true
            });

            var notifier = new RecordingNotifier();
            using var service = new ReminderNotificationService(repository, notifier);
            await service.CheckDueRemindersAsync(now);
            Assert.Equal(1, notifier.Count);

            var occurrenceKey = $"durable-snooze:{start.UtcTicks}:30";
            await service.SnoozeAsync(occurrenceKey, 1, now);

            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TRIGGER fail_reminder_snooze_delete
                    BEFORE DELETE ON settings
                    WHEN OLD.key = 'reminder:snoozed'
                    BEGIN
                        SELECT RAISE(ABORT, 'intentional reminder snooze delete failure');
                    END;
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await service.CheckDueRemindersAsync(now.AddMinutes(2));
            Assert.Equal(2, notifier.Count);

            await service.CheckDueRemindersAsync(now.AddMinutes(3));

            Assert.Equal(2, notifier.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
        }
    }

    private sealed class RecordingNotifier : IReminderNotifier
    {
        public int Count { get; private set; }

        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            return Task.CompletedTask;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}