using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class ScheduleEditingRegressionTests
{
    private static readonly string AppRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App"));

    [Fact]
    public async Task SidePanelDoubleClick_OpensScheduleEditorWithoutDateNavigation()
    {
        var source = await ReadReliabilitySourceAsync();
        var initialization = ExtractMethod(
            source,
            "protected override void OnContentRendered",
            "private async void ReliableSelectedDayEventsGrid_MouseDoubleClick");
        var handler = ExtractMethod(
            source,
            "private async void ReliableSelectedDayEventsGrid_MouseDoubleClick",
            "private async void ReliableEventSegment_PreviewMouseLeftButtonDown");

        Assert.Contains("grid.MouseDoubleClick -= SelectedDayEventsGrid_MouseDoubleClick;", initialization, StringComparison.Ordinal);
        Assert.Contains("grid.MouseDoubleClick += ReliableSelectedDayEventsGrid_MouseDoubleClick;", initialization, StringComparison.Ordinal);
        Assert.Contains("_viewModel.SelectEvent(calendarEvent, selectEventDay: false);", handler, StringComparison.Ordinal);
        Assert.Contains("await OpenSelectedScheduleEditorReliablyAsync();", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateToDateAsync", handler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WeekScheduleDoubleClick_ClearsPendingDragBeforeOpeningEditor()
    {
        var source = await ReadReliabilitySourceAsync();
        var handler = ExtractMethod(
            source,
            "private async void ReliableEventSegment_PreviewMouseLeftButtonDown",
            "private async Task OpenSelectedScheduleEditorReliablyAsync");

        var clearStart = handler.IndexOf("_dragStartPoint = null;", StringComparison.Ordinal);
        var clearSegment = handler.IndexOf("_dragSegment = null;", StringComparison.Ordinal);
        var open = handler.IndexOf("await OpenSelectedScheduleEditorReliablyAsync();", StringComparison.Ordinal);
        Assert.True(clearStart >= 0 && clearSegment > clearStart && open > clearSegment);
    }

    [Fact]
    public async Task ScheduleEditor_RestoresCapturedIdentityBeforeApplyingAcceptedValues()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        var method = ExtractMethod(
            source,
            "private async Task ShowScheduleDialogAsync(bool forceNew = false)",
            "private async Task ShowSelectedTodoDialogAsync");

        var restore = method.IndexOf("RestoreScheduleSaveIdentity(editingEvent);", StringComparison.Ordinal);
        var applyCalendar = method.IndexOf("_viewModel.EditorCalendarId = result.CalendarId;", StringComparison.Ordinal);
        var save = method.IndexOf("await _viewModel.SaveCurrentEventAsync(recurrenceScope);", StringComparison.Ordinal);
        Assert.True(restore >= 0 && applyCalendar > restore && save > applyCalendar,
            "The captured edit identity must be restored before accepted dialog values are applied and saved.");
    }

    [Fact]
    public async Task ScheduleEditor_SaveIdentityHandlesBothNewAndExistingSchedules()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        var method = ExtractMethod(
            source,
            "private void RestoreScheduleSaveIdentity",
            "private async Task ShowSelectedTodoDialogAsync");

        Assert.Contains("if (editingEvent is null)", method, StringComparison.Ordinal);
        Assert.Contains("_viewModel.SelectedEvent = null;", method, StringComparison.Ordinal);
        Assert.Contains("string.Equals(_viewModel.SelectedEvent?.Id, editingEvent.Id, StringComparison.Ordinal)", method, StringComparison.Ordinal);
        Assert.Contains("_viewModel.SelectEvent(editingEvent, selectEventDay: false);", method, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduleEditorReliability_DoesNotDependOnWindowLifecycleToPreserveIdentity()
    {
        var source = await ReadReliabilitySourceAsync();

        Assert.DoesNotContain("PropertyChanged +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Deactivated +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FindScheduleEditorWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleEditorWindow_Closed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_activeScheduleEditingEvent", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduleEditor_CalendarSelectionIsPreparedBeforeModalAttachment()
    {
        var reliabilitySource = await ReadReliabilitySourceAsync();
        var mainWindowSource = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.xaml.cs"));

        Assert.Contains("_viewModel.ResolveScheduleEditorCalendarId(editingEvent)", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("_viewModel.CreateScheduleEditorCalendarOptions(editingEvent, scheduleCalendarId)", mainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureScheduleEditorCalendarSelection", reliabilitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("FindVisualDescendants<ComboBox>", reliabilitySource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MonthScheduleDoubleClick_UsesIdentityProtectedScheduleEditorPath()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.MonthEventLayer.cs"));

        Assert.Contains("if (segment.Event.IsTodoLike)", source, StringComparison.Ordinal);
        Assert.Contains("await OpenSelectedScheduleEditorReliablyAsync();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveScheduleEditorCalendarId_NewOrMissingCalendar_UsesConcreteRegisteredCalendar()
    {
        var viewModel = CreateViewModel();
        viewModel.AvailableCalendars.Add(new GoogleCalendarSelectionItem { Id = "calendar-a", Summary = "A", IsSelected = true });
        viewModel.AvailableCalendars.Add(new GoogleCalendarSelectionItem { Id = "calendar-b", Summary = "B" });
        viewModel.EditorCalendarId = GoogleCalendarDefaults.PrimaryCalendarId;

        Assert.Equal("calendar-a", viewModel.ResolveScheduleEditorCalendarId(null));
        Assert.Equal("calendar-a", viewModel.ResolveScheduleEditorCalendarId(new CalendarEvent { CalendarId = "" }));
        Assert.Equal("calendar-a", viewModel.ResolveScheduleEditorCalendarId(new CalendarEvent { CalendarId = GoogleCalendarDefaults.PrimaryCalendarId }));
        Assert.Equal("hidden-calendar", viewModel.ResolveScheduleEditorCalendarId(new CalendarEvent { CalendarId = "hidden-calendar" }));
    }

    [Fact]
    public async Task ResolveScheduleEditorCalendarId_NewSchedulePrefersPersistedActiveCalendarOverTransientEditorCalendar()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveSettingsAsync(new AppSettings
        {
            VisibleCalendarIds = ["calendar-a", "calendar-b"],
            ActiveCalendarId = "calendar-b"
        });
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        viewModel.EditorCalendarId = "calendar-a";

        Assert.Equal("calendar-b", viewModel.ResolveScheduleEditorCalendarId(null));
    }

    [Fact]
    public void CreateScheduleEditorCalendarOptions_PrimaryAliasAndUnavailableCurrentCalendarRemainSelectable()
    {
        var viewModel = CreateViewModel();
        viewModel.AvailableCalendars.Add(new GoogleCalendarSelectionItem { Id = "calendar-a", Summary = "A", IsSelected = true });

        var primaryOptions = viewModel.CreateScheduleEditorCalendarOptions(
            new CalendarEvent { CalendarId = GoogleCalendarDefaults.PrimaryCalendarId },
            GoogleCalendarDefaults.PrimaryCalendarId);
        var unavailableOptions = viewModel.CreateScheduleEditorCalendarOptions(
            new CalendarEvent { CalendarId = "archived-calendar" },
            "archived-calendar");

        Assert.Contains(primaryOptions, item => item.Id == GoogleCalendarDefaults.PrimaryCalendarId && item.Summary == "メインカレンダー");
        Assert.Contains(primaryOptions, item => item.Id == "calendar-a");
        Assert.Contains(unavailableOptions, item => item.Id == "archived-calendar");
        Assert.Contains(unavailableOptions, item => item.Id == "calendar-a");
        Assert.False(ReferenceEquals(viewModel.AvailableCalendars, primaryOptions));
    }

    [Fact]
    public async Task ScheduleHistoryAccess_UsesInitializedInMemoryCacheInsteadOfReloadingSqliteForEveryDialog()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "ViewModels", "MainViewModel.Settings.cs"));
        var title = ExtractMethod(
            source,
            "public Task<IReadOnlyList<string>> LoadScheduleTitleHistoryAsync",
            "public Task<IReadOnlyList<string>> LoadScheduleLocationHistoryAsync");
        var location = ExtractMethod(
            source,
            "public Task<IReadOnlyList<string>> LoadScheduleLocationHistoryAsync",
            "public async Task ClearScheduleTitleHistoryAsync");

        Assert.Contains("Task.FromResult(_scheduleTitleHistory)", title, StringComparison.Ordinal);
        Assert.Contains("Task.FromResult(_scheduleLocationHistory)", location, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadScheduleHistoryAsync", title, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadScheduleHistoryAsync", location, StringComparison.Ordinal);
    }

    private static MainViewModel CreateViewModel()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        return new MainViewModel(repository, new GoogleCalendarSyncService(repository));
    }

    private static Task<string> ReadReliabilitySourceAsync() =>
        File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.ScheduleEditorReliability.cs"));

    private static string ExtractMethod(string source, string startMarker, string nextMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{startMarker} was not found.");
        var end = source.IndexOf(nextMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"{nextMarker} was not found after {startMarker}.");
        return source[start..end];
    }
}
