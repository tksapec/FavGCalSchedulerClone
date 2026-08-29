using FavGCalSchedulerClone.App.Services;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReminderFailureContainmentRegressionTests
{
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
