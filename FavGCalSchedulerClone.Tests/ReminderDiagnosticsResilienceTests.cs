using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReminderDiagnosticsResilienceTests
{
    [Fact]
    public async Task LoadDiagnosticsAsync_NormalizesNullCandidatesToEmptyCollection()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var snapshot = new ReminderMonitoringSnapshot(
                false, null, DateTimeOffset.Now, null,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, []);
            var json = JsonSerializer.Serialize(snapshot)
                .Replace("\"Candidates\":[]", "\"Candidates\":null", StringComparison.Ordinal);
            await repository.SaveSettingValueAsync("reminder:diagnostics", json);

            using var service = new ReminderNotificationService(repository);

            var exception = await Record.ExceptionAsync(service.LoadDiagnosticsAsync);
            var loaded = await service.LoadDiagnosticsAsync();

            Assert.Null(exception);
            Assert.NotNull(loaded.Candidates);
            Assert.Empty(loaded.Candidates);
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
