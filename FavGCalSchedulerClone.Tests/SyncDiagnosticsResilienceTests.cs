using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class SyncDiagnosticsResilienceTests
{
    [Fact]
    public async Task LoadDiagnosticsAsync_IgnoresNullStoredHistoryAndFailureEntries()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await repository.SaveSettingValueAsync("sync:history", "[null]");
            await repository.SaveSettingValueAsync("sync:last-failures", "[null]");
            var service = new GoogleCalendarSyncService(repository);

            var exception = await Record.ExceptionAsync(() => service.LoadDiagnosticsAsync(new AppSettings()));
            var diagnostics = await service.LoadDiagnosticsAsync(new AppSettings());

            Assert.Null(exception);
            Assert.Empty(diagnostics.History);
            Assert.Empty(diagnostics.Failures);
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
