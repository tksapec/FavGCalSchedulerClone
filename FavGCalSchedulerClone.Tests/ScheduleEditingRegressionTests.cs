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
    public async Task SidePanelDoubleClick_OpensScheduleEditorWithoutWaitingForDateNavigation()
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
    public async Task ScheduleEditor_RestoresEditingIdentityBeforeDialogClosedReturns()
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
        Assert.True(restore >= 0 && clear > restore, "The original event identity must be restored synchronously before the modal window returns to the save path.");
    }

    [Fact]
    public async Task ScheduleEditor_EditFallbackNeverUsesLastSelectionForNewScheduleWindow()
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
    public async Task ScheduleEditor_BlankPrimaryCalendarSelectsConcreteRegisteredCalendar()
    {
        var source = await ReadReliabilitySourceAsync();
        var method = ExtractMethod(
            source,
            "private void EnsureScheduleEditorCalendarSelection",
            "private Window? FindScheduleEditorWindow");

        Assert.Contains("GoogleCalendarDefaults.PrimaryCalendarId", method, StringComparison.Ordinal);
        Assert.Contains("calendarSelector.SelectedValue = calendarId;", method, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(combo.ItemsSource, _viewModel.AvailableCalendars)", method, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MonthScheduleDoubleClick_UsesReliableScheduleEditorPath()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.MonthEventLayer.cs"));

        Assert.Contains("if (segment.Event.IsTodoLike)", source, StringComparison.Ordinal);
        Assert.Contains("await OpenSelectedScheduleEditorReliablyAsync();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveScheduleEditorCalendarId_NewOrPrimaryAlias_UsesRegisteredSelectedCalendar()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        viewModel.AvailableCalendars.Add(new GoogleCalendarSelectionItem { Id = "calendar-a", Summary = "A", IsSelected = true });
        viewModel.AvailableCalendars.Add(new GoogleCalendarSelectionItem { Id = "calendar-b", Summary = "B" });
        viewModel.EditorCalendarId = GoogleCalendarDefaults.PrimaryCalendarId;

        Assert.Equal("calendar-a", viewModel.ResolveScheduleEditorCalendarId(null));
        Assert.Equal("calendar-a", viewModel.ResolveScheduleEditorCalendarId(new CalendarEvent { CalendarId = "" }));
        Assert.Equal("calendar-a", viewModel.ResolveScheduleEditorCalendarId(new CalendarEvent { CalendarId = GoogleCalendarDefaults.PrimaryCalendarId }));
        Assert.Equal("hidden-calendar", viewModel.ResolveScheduleEditorCalendarId(new CalendarEvent { CalendarId = "hidden-calendar" }));
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
