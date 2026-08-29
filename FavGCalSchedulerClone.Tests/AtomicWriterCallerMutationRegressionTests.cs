using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class AtomicWriterCallerMutationRegressionTests
{
    [Fact]
    public async Task SaveEventsAsync_WhenTransactionRollsBack_DoesNotMutateCallerOwnedEvents()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"atomic-writer-caller-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TRIGGER fail_second_atomic_write
                    BEFORE INSERT ON events
                    WHEN NEW.id = 'second-event'
                    BEGIN
                        SELECT RAISE(ABORT, 'intentional atomic writer failure');
                    END;
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var originalUpdatedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var first = CreateEvent("first-event", originalUpdatedAt);
            var second = CreateEvent("second-event", originalUpdatedAt);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                CalendarRepositoryAtomicWriter.SaveEventsAsync(repository, [first, second]));

            Assert.Equal(originalUpdatedAt, first.UpdatedAt);
            Assert.Equal(originalUpdatedAt, second.UpdatedAt);
            Assert.Null(first.DirtyFields);
            Assert.Null(second.DirtyFields);
            Assert.False(first.IsTodoLike);
            Assert.False(second.IsTodoLike);
            Assert.Empty(await repository.LoadEventsAsync(
                new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero),
                includeDeleted: true));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
        }
    }

    private static CalendarEvent CreateEvent(string id, DateTimeOffset updatedAt)
    {
        return new CalendarEvent
        {
            Id = id,
            Title = id,
            Start = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero),
            UpdatedAt = updatedAt,
            DirtyFields = null,
            IsTodoLike = false
        };
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
