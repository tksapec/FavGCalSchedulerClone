using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class BranchIntegrationRegressionTests
{
    [Fact]
    public async Task AppDeactivation_KeepsSelectionWhileOwnedDialogIsVisible()
    {
        var source = await ReadAppSourceAsync("App.xaml.cs");

        Assert.Contains("window.IsVisible && ReferenceEquals(window.Owner, MainWindow)", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InlineYearSearch_FromWeekViewSwitchesToMonthAndRestoresWeekOnClose()
    {
        var viewModel = await CreateViewModelAsync();
        viewModel.CurrentViewMode = CalendarViewMode.Week;

        await viewModel.RunCurrentYearSearchAsync();

        Assert.True(viewModel.IsMonthView);
        Assert.True(viewModel.IsSearchResultsVisible);

        viewModel.ClearCurrentYearSearchCommand.Execute(null);

        Assert.Equal(CalendarViewMode.Week, viewModel.CurrentViewMode);
        Assert.False(viewModel.IsSearchResultsVisible);
    }

    [Theory]
    [InlineData("23:30", 60, "00:30", 1)]
    [InlineData("23:30", 120, "01:30", 1)]
    [InlineData("10:00", 60, "11:00", 0)]
    public void ScheduleEditor_DurationShortcutTracksDateOffset(
        string startTime,
        int durationMinutes,
        string expectedEndTime,
        int expectedDayOffset)
    {
        var result = ScheduleEditorDialog.TryCreateEndTimeFromDuration(
            startTime,
            TimeSpan.FromMinutes(durationMinutes),
            out var endTime,
            out var dayOffset);

        Assert.True(result);
        Assert.Equal(expectedEndTime, endTime);
        Assert.Equal(expectedDayOffset, dayOffset);
    }

    [Fact]
    public void ScheduleEditor_StartTimeShiftTracksMidnightCrossing()
    {
        var result = ScheduleEditorDialog.TryShiftEndTimeForStartChange(
            "22:30",
            "23:30",
            "23:45",
            out var endTime,
            out var dayOffset);

        Assert.True(result);
        Assert.Equal("00:45", endTime);
        Assert.Equal(1, dayOffset);
    }

    [Fact]
    public async Task ScheduleEditor_AppliesDateOffsetToTheEndDateControls()
    {
        var source = await ReadAppSourceAsync("Views", "Dialogs", "ScheduleEditorDialog.cs");

        Assert.Contains("endDate.SelectedDate = selectedStartDate.Date.AddDays(endDateOffset);", source, StringComparison.Ordinal);
        Assert.Contains("endDate.SelectedDate = selectedEndDate.Date.AddDays(endDateOffset);", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncDiagnostics_ReevaluatesActionStateAfterRefresh()
    {
        var source = await ReadAppSourceAsync("Views", "Dialogs", "SyncDialogs.cs");

        Assert.Contains("Action updateRetryFailuresState = static () => { };", source, StringComparison.Ordinal);
        Assert.Contains("Action updateDirtyActionState = static () => { };", source, StringComparison.Ordinal);
        Assert.Contains("updateRetryFailuresState();", source, StringComparison.Ordinal);
        Assert.Contains("updateDirtyActionState();", source, StringComparison.Ordinal);
        Assert.Contains("updateDirtyActionState = UpdateDirtyActionState;", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitAddScheduleEntryPoints_AlwaysForceTheNewSchedulePath()
    {
        var mainWindow = await ReadAppSourceAsync("MainWindow.xaml.cs");
        var settings = await ReadAppSourceAsync("ViewModels", "MainViewModel.Settings.cs");

        Assert.Contains("private async Task ShowScheduleDialogAsync(bool forceNew = false)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("var editingEvent = forceNew ? null : _viewModel.SelectedEvent;", mainWindow, StringComparison.Ordinal);
        Assert.Contains("() => RunAsOwnedModalAsync(() => ShowScheduleDialogAsync(forceNew: true))", mainWindow, StringComparison.Ordinal);
        Assert.Contains("RunUiActionAsync(() => ShowScheduleDialogAsync(forceNew: true), \"ContextMenu.AddSchedule\")", mainWindow, StringComparison.Ordinal);
        Assert.Contains("RunUiActionAsync(() => ShowScheduleDialogAsync(forceNew: true), nameof(AddScheduleMenu_Click))", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_showAddScheduleAsync = showAddScheduleAsync;", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedEvent = null;\n            await showAddScheduleAsync();", settings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsDialog_UsesFriendlyDisplayOptionsInsteadOfRawEnumsAndNumbers()
    {
        var source = await ReadAppSourceAsync("Views", "Dialogs", "SettingsDialog.cs");

        Assert.Contains("SettingsDisplayOptions.CalendarViewModes", source, StringComparison.Ordinal);
        Assert.Contains("SettingsDisplayOptions.FontSizes", source, StringComparison.Ordinal);
        Assert.Contains("SettingsDisplayOptions.WeekdayDisplayTypes", source, StringComparison.Ordinal);
        Assert.Contains("SettingsDisplayOptions.TodoPeriods", source, StringComparison.Ordinal);
        Assert.Contains("SettingsDisplayOptions.ConflictPolicies", source, StringComparison.Ordinal);
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

    private static Task<string> ReadAppSourceAsync(params string[] parts)
    {
        var pathParts = new[] { GetRepositoryRoot(), "FavGCalSchedulerClone.App" }
            .Concat(parts)
            .ToArray();
        return File.ReadAllTextAsync(Path.Combine(pathParts));
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FavGCalSchedulerClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
