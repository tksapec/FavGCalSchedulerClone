using FavGCalSchedulerClone.App.Services;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReminderHistoryResilienceTests
{
    [Fact]
    public async Task LoadHistoryAsync_IgnoresNullHistoryEntries()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await repository.SaveSettingValueAsync("reminder:history", "[null]");
            using var service = new ReminderNotificationService(repository);

            var exception = await Record.ExceptionAsync(service.LoadHistoryAsync);
            var history = await service.LoadHistoryAsync();

            Assert.Null(exception);
            Assert.Empty(history);
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
