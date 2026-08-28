using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

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

    [Fact]
    public async Task ScheduleEditor_TimeShortcutsTrackDateOffsetAcrossMidnight()
    {
        var source = await ReadAppSourceAsync("Views", "Dialogs", "ScheduleEditorDialog.cs");

        Assert.Contains("out int dayOffset", source, StringComparison.Ordinal);
        Assert.Contains("dayOffset = end.Days;", source, StringComparison.Ordinal);
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
    public async Task AddScheduleCommand_AlwaysUsesNewSchedulePathEvenWhenAnEventIsSelected()
    {
        var source = await ReadAppSourceAsync("MainWindow.xaml.cs");

        Assert.Contains("ShowScheduleDialogAsync(forceNew: true)", source, StringComparison.Ordinal);
        Assert.Contains("private async Task ShowScheduleDialogAsync(bool forceNew = false)", source, StringComparison.Ordinal);
        Assert.Contains("var editingEvent = forceNew ? null : _viewModel.SelectedEvent;", source, StringComparison.Ordinal);
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

    private static Task<string> ReadAppSourceAsync(params string[] parts) =>
        File.ReadAllTextAsync(Path.Combine(
            [
                GetRepositoryRoot(),
                "FavGCalSchedulerClone.App",
                .. parts
            ]));

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
