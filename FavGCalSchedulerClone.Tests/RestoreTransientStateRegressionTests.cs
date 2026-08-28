using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class RestoreTransientStateRegressionTests
{
    [Fact]
    public async Task Restore_ClearsUndoClipboardSearchAndOldNavigationSelectionState()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"restore-transient-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var targetPath = Path.Combine(directory, "target.db");
        var backupPath = Path.Combine(directory, "backup.zip");
        var backupService = new BackupService();
        var restoredMonth = new DateTime(2026, 2, 1);

        var sourceRepository = new CalendarRepository(sourcePath);
        await sourceRepository.InitializeAsync();
        await sourceRepository.SaveSettingsAsync(new AppSettings { DisplayMonth = restoredMonth });
        await backupService.CreateBackupAsync(sourcePath, backupPath);

        var targetRepository = new CalendarRepository(targetPath);
        await targetRepository.InitializeAsync();
        var oldDate = new DateTime(2026, 8, 20);
        var oldEvent = new CalendarEvent
        {
            Id = "pre-restore-event",
            CalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
            Title = "pre restore",
            Start = new DateTimeOffset(oldDate.AddHours(9)),
            End = new DateTimeOffset(oldDate.AddHours(10)),
            IsAllDay = false,
            IsDirty = true
        };
        await targetRepository.SaveEventAsync(oldEvent);

        var viewModel = new MainViewModel(
            targetRepository,
            new GoogleCalendarSyncService(targetRepository),
            backupService,
            new CalendarCsvService(),
            new FavGCalSchedulerImportService(targetRepository));
        await viewModel.InitializeAsync();
        await viewModel.NavigateToDateAsync(oldDate);
        viewModel.SelectEvent(oldEvent, selectEventDay: false);
        viewModel.CopySelectedEventLabel();
        await viewModel.PasteEventLabelAsync(oldDate.AddDays(1));
        viewModel.SearchQuery = "pre restore";
        await viewModel.RunCurrentYearSearchAsync();

        Assert.True(viewModel.CanUndoLastChange);
        Assert.True(viewModel.CanPasteEventLabel);
        Assert.True(viewModel.IsSearchResultsVisible);
        Assert.NotEmpty(viewModel.SearchResults);

        await viewModel.RestoreAllCalendarsAsync(backupPath);

        Assert.False(viewModel.CanUndoLastChange);
        Assert.False(viewModel.CanPasteEventLabel);
        Assert.False(viewModel.IsSearchResultsVisible);
        Assert.Empty(viewModel.SearchResults);
        Assert.Null(viewModel.SelectedSearchResult);
        Assert.Equal(restoredMonth, viewModel.CurrentMonth);
        Assert.True(viewModel.SelectedDay is null || viewModel.SelectedDay.Date.Month == restoredMonth.Month);
    }
}
