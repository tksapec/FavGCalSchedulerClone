using FavGCalSchedulerClone.App.Services;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReminderFailureContainmentRegressionTests
{
    [Fact]
    public async Task CheckDueRemindersAsync_ClearsPersistedErrorAfterSuccessfulCheck()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await repository.SaveSettingValueAsync("reminder:last-error", "2026-09-22T08:08:53.5180550+09:00 old error");
            using var service = new ReminderNotificationService(repository);

            await service.CheckDueRemindersAsync(DateTimeOffset.Now);

            Assert.Null(service.CurrentDiagnostics.LastError);
            Assert.Null(await repository.LoadSettingValueAsync("reminder:last-error"));
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
    public async Task CheckDueRemindersAsync_DoesNotRethrowWhenErrorPersistenceAlsoFails()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            using var service = new ReminderNotificationService(repository);

            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    DROP TABLE events;
                    CREATE TRIGGER fail_reminder_error_write
                    BEFORE INSERT ON settings
                    BEGIN
                        SELECT RAISE(ABORT, 'intentional reminder diagnostic write failure');
                    END;
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var exception = await Record.ExceptionAsync(() => service.CheckDueRemindersAsync(DateTimeOffset.Now));

            Assert.Null(exception);
            Assert.Contains("no such table", service.CurrentDiagnostics.LastError ?? "", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
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
