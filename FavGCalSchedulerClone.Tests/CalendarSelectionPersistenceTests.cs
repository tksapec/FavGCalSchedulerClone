using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarSelectionPersistenceTests
{
    [Fact]
    public async Task ApplyCalendarSelectionAsync_WhenPersistenceFails_RestoresPreviousSelectionAndSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new CalendarRepository(directory);
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            viewModel.AvailableCalendars.Add(new GoogleCalendarSelectionItem
            {
                Id = "primary",
                Summary = "Primary",
                IsSelected = true
            });
            viewModel.AvailableCalendars.Add(new GoogleCalendarSelectionItem
            {
                Id = "team",
                Summary = "Team",
                IsSelected = false
            });

            viewModel.AvailableCalendars[0].IsSelected = false;
            viewModel.AvailableCalendars[1].IsSelected = true;

            await Assert.ThrowsAnyAsync<Exception>(() => viewModel.ApplyCalendarSelectionAsync());

            Assert.True(viewModel.AvailableCalendars.Single(item => item.Id == "primary").IsSelected);
            Assert.False(viewModel.AvailableCalendars.Single(item => item.Id == "team").IsSelected);
            var settings = viewModel.CreateSettingsSnapshot();
            Assert.Empty(settings.VisibleCalendarIds);
            Assert.Equal("primary", settings.ActiveCalendarId);
            Assert.Equal("primary", viewModel.EditorCalendarId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
