using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class DescriptionPersistenceRegressionTests
{
    [Fact]
    public async Task ScheduleSave_PreservesPastedDescriptionWhitespaceAndNewlines()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "calendar.db");
        var repository = new CalendarRepository(databasePath);
        try
        {
            await repository.InitializeAsync();
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            var date = new DateTime(2026, 9, 15);
            var description = "  copied first line\r\nsecond line\r\n  ";

            viewModel.BeginNewEvent(date);
            viewModel.Title = "clipboard preservation";
            viewModel.Description = description;
            viewModel.IsAllDay = true;
            viewModel.StartDate = date;
            viewModel.EndDate = date;

            await viewModel.SaveCurrentEventAsync();

            Assert.NotNull(viewModel.SelectedEvent);
            Assert.Equal(description, viewModel.SelectedEvent.Description);
        }
        finally
        {
            await repository.BeginMaintenanceAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
