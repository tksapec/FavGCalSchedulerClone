using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class OAuthSettingsReloadRegressionTests
{
    [Fact]
    public async Task SaveApplicationSettingsAsync_WhenOAuthPathChanges_ReloadsAvailableCalendarsAfterPersistence()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "calendar.db");
        try
        {
            var repository = new CalendarRepository(databasePath);
            await repository.InitializeAsync();
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            viewModel.AvailableCalendars.Add(new GoogleCalendarSelectionItem
            {
                Id = "stale-calendar",
                Summary = "Stale",
                IsSelected = false
            });
            Assert.Contains(viewModel.AvailableCalendars, item => item.Id == "stale-calendar");

            var changed = viewModel.CreateSettingsSnapshot();
            changed.OAuthClientJsonPath = Path.Combine(directory, "missing-client.json");

            await viewModel.SaveApplicationSettingsAsync(changed);

            Assert.DoesNotContain(viewModel.AvailableCalendars, item => item.Id == "stale-calendar");
            Assert.Equal(changed.OAuthClientJsonPath, viewModel.OAuthClientJsonPath);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
