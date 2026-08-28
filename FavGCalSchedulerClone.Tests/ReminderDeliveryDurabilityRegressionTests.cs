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
