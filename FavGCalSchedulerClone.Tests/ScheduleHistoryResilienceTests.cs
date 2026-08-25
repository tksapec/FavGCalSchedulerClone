using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class ScheduleHistoryResilienceTests
{
    [Fact]
    public async Task InitializeAsync_IgnoresCorruptedScheduleHistoryJson()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await repository.SaveSettingValueAsync("schedule:title-history", "{not-json");
            await repository.SaveSettingValueAsync("schedule:location-history", "[also-not-json");

            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));

            var exception = await Record.ExceptionAsync(viewModel.InitializeAsync);

            Assert.Null(exception);
            Assert.Empty(await viewModel.LoadScheduleTitleHistoryAsync());
            Assert.Empty(await viewModel.LoadScheduleLocationHistoryAsync());
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
