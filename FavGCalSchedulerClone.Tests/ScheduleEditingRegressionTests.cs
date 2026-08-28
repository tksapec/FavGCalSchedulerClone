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
    public async Task ScheduleEditor_RestoresEditingIdentityBeforeModalReturnsToSavePath()
    {
        var source = await ReadReliabilitySourceAsync();
        var attach = ExtractMethod(
            source,
            "private void TryAttachScheduleEditorWindow",
            "private void ScheduleEditorWindow_Closed");
        var closed = ExtractMethod(
            source,
            "private void ScheduleEditorWindow_Closed",
            "private void PreserveScheduleEditingIdentity");

        Assert.Contains("window.Closed += ScheduleEditorWindow_Closed;", attach, StringComparison.Ordinal);
        var restore = closed.IndexOf("RestoreScheduleEditingIdentity();", StringComparison.Ordinal);
        var clear = closed.IndexOf("_activeScheduleEditingEvent = null;", StringComparison.Ordinal);
        Assert.True(restore >= 0 && clear > restore, "The original event identity must be restored synchronously before the modal window returns to the existing save path.");
    }

    [Fact]
    public async Task ScheduleEditor_DoesNotReplaceFallbackIdentityWithTransientSelectionWhileOpen()
    {
        var source = await ReadReliabilitySourceAsync();
        var method = ExtractMethod(
            source,
            "private void PreserveScheduleEditingIdentity",
            "private void RestoreScheduleEditingIdentity");

        var findWindow = method.IndexOf("var window = FindScheduleEditorWindow();", StringComparison.Ordinal);
        var updateLast = method.IndexOf("_lastSelectedScheduleEvent = selected;", StringComparison.Ordinal);
        var returnWhenNoWindow = method.IndexOf("return;", updateLast, StringComparison.Ordinal);
        var activateFallback = method.IndexOf("_activeScheduleEditingEvent = _lastSelectedScheduleEvent;", StringComparison.Ordinal);
        Assert.True(findWindow >= 0 && updateLast > findWindow && returnWhenNoWindow > updateLast && activateFallback > returnWhenNoWindow);
    }

    [Fact]
    public async Task ScheduleEditor_NewWindowNeverFallsBackToPreviousEditingIdentity()
    {
        var source = await ReadReliabilitySourceAsync();
        var attach = ExtractMethod(
            source,
            "private void TryAttachScheduleEditorWindow",
            "private void ScheduleEditorWindow_Closed");

        Assert.Contains("string.Equals(window.Title, \"スケジュールの編集\", StringComparison.Ordinal)", attach, StringComparison.Ordinal);
        Assert.Contains(": _lastSelectedScheduleEvent;", attach, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduleEditor_NewWindowKeepsSelectedEventNullWhileOpen()
    {
        var source = await ReadReliabilitySourceAsync();
        var attach = ExtractMethod(
            source,
            "private void TryAttachScheduleEditorWindow",
            "private void ScheduleEditorWindow_Closed");
        var restore = ExtractMethod(
            source,
            "private void RestoreScheduleEditingIdentity",
            "private Window? FindScheduleEditorWindow");

        Assert.Contains("_scheduleEditorIsNew = string.Equals(window.Title, \"スケジュールの追加\", StringComparison.Ordinal);", attach, StringComparison.Ordinal);
        Assert.Contains("if (_scheduleEditorIsNew)", restore, StringComparison.Ordinal);
        Assert.Contains("_viewModel.SelectedEvent = null;", restore, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduleEditor_ChoosesRecurrenceScopeAndStabilizesIdentityBeforeApplyingResult()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        var method = ExtractMethod(
            source,
            "private async Task ShowScheduleDialogAsync",
            "private async Task ShowSelectedTodoDialogAsync");

        var prompt = method.IndexOf("recurrenceScope = PromptRecurrenceScope(false);", StringComparison.Ordinal);
        var stabilize = method.IndexOf("EnsureScheduleSaveIdentity(editingEvent);", StringComparison.Ordinal);
        var apply = method.IndexOf("_viewModel.EditorCalendarId = result.CalendarId;", StringComparison.Ordinal);
        var save = method.IndexOf("await _viewModel.SaveCurrentEventAsync(recurrenceScope);", StringComparison.Ordinal);

        Assert.True(prompt >= 0 && stabilize > prompt && apply > stabilize && save > apply,
            "Recurring scope cancellation must happen before editor state is changed, and the original/new identity must be stabilized immediately before applying the accepted result.");
    }

    [Fact]
    public async Task ScheduleEditor_SaveBoundaryHandlesNewAndExistingIdentityExplicitly()
    {
        var source = await ReadReliabilitySourceAsync();
        var method = ExtractMethod(
            source,
            "private void EnsureScheduleSaveIdentity",
            "private static bool SameScheduleOccurrence");

        Assert.Contains("if (editingEvent is null)", method, StringComparison.Ordinal);
        Assert.Contains("_viewModel.SelectedEvent = null;", method, StringComparison.Ordinal);
        Assert.Contains("SameScheduleOccurrence(current, editingEvent)", method, StringComparison.Ordinal);
        Assert.Contains("_viewModel.SelectEvent(editingEvent, selectEventDay: false);", method, StringComparison.Ordinal);
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
