using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReviewRegressionTests
{
    [Fact]
    public async Task BulkReminderEditor_UsesReminderOptionMinutesBeforeStartValue()
    {
        var code = await File.ReadAllTextAsync(AppSourcePath("Views", "Dialogs", "BulkEventUpdateDialog.cs"));

        Assert.Contains("SelectedValuePath = nameof(ReminderOption.MinutesBeforeStart)", code);
    }

    [Fact]
    public async Task BulkReminderEditor_AllowsNotificationOffAndDisablesChannelsWithoutATime()
    {
        var code = await File.ReadAllTextAsync(AppSourcePath("Views", "Dialogs", "BulkEventUpdateDialog.cs"));

        Assert.DoesNotContain("reminderEnabled.IsChecked == true && minutes.SelectedValue is int", code);
        Assert.Contains("var reminderHasTime = minutes.SelectedValue is int;", code);
        Assert.Contains("reminderHasTime && appReminder.IsChecked == true", code);
        Assert.Contains("reminderHasTime && emailReminder.IsChecked == true", code);
    }

    [Fact]
    public async Task InlineYearSearch_FromWeekViewSwitchesToMonthSoResultsPaneIsVisible()
    {
        var viewModel = await CreateViewModelAsync();
        viewModel.CurrentViewMode = CalendarViewMode.Week;

        await viewModel.RunCurrentYearSearchAsync();

        Assert.True(viewModel.IsMonthView);
        Assert.True(viewModel.IsSearchResultsVisible);
    }

    [Fact]
    public async Task ReleasePublish_CleansPreviousOutputBeforePublishing()
    {
        var scriptPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "scripts",
            "publish-release.ps1"));
        var code = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("Remove-Item -LiteralPath $publishDirectory -Recurse -Force", code);
    }

    [Fact]
    public async Task SyncDiagnostics_ReevaluatesFailureRetryStateAfterRefresh()
    {
        var code = await File.ReadAllTextAsync(AppSourcePath("Views", "Dialogs", "SyncDialogs.cs"));

        Assert.Contains("updateRetryFailuresState();", code);
        Assert.Contains("currentDiagnostics.Failures.Any", code);
    }

    [Fact]
    public void ScheduleDurationShortcut_ReportsNextDayAcrossMidnight()
    {
        var success = ScheduleEditorDialog.TryCreateEndTimeFromDuration(
            "23:30",
            TimeSpan.FromHours(1),
            out var endTime,
            out var dayOffset);

        Assert.True(success);
        Assert.Equal("00:30", endTime);
        Assert.Equal(1, dayOffset);
    }

    [Fact]
    public void ScheduleStartTimeShift_ReportsEndDateShiftAcrossMidnight()
    {
        var success = ScheduleEditorDialog.TryShiftEndTimeForStartChange(
            "23:00",
            "23:30",
            "23:30",
            out var endTime,
            out var dayOffset);

        Assert.True(success);
        Assert.Equal("00:00", endTime);
        Assert.Equal(1, dayOffset);
    }

    private static async Task<MainViewModel> CreateViewModelAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static string AppSourcePath(params string[] parts)
    {
        return Path.GetFullPath(Path.Combine(
            [
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "FavGCalSchedulerClone.App",
                .. parts
            ]));
    }
}
