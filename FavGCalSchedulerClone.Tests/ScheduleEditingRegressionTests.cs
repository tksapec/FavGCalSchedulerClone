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
        var handler = ExtractMethod(
            source,
            "private async void ReliableSelectedDayEventsGrid_MouseDoubleClick",
            "private async void ReliableMonthDayList_MouseDoubleClick");

        Assert.Contains("_viewModel.SelectEvent(calendarEvent, selectEventDay: false);", handler, StringComparison.Ordinal);
        Assert.Contains("await ShowScheduleDialogReliablyAsync();", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateToDateAsync", handler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduleEditor_PreservesCapturedEditingIdentityWhileDialogIsOpen()
    {
        var source = await ReadReliabilitySourceAsync();
        var wrapper = ExtractMethod(
            source,
            "private async Task ShowScheduleDialogReliablyAsync",
            "private void PreserveScheduleEditingIdentity");
        var guard = ExtractMethod(
            source,
            "private void PreserveScheduleEditingIdentity",
            "private void EnsureScheduleEditorCalendar");

        Assert.Contains("var editingEvent = _viewModel.SelectedEvent;", wrapper, StringComparison.Ordinal);
        Assert.Contains("_activeScheduleEditingEvent = editingEvent;", wrapper, StringComparison.Ordinal);
        Assert.Contains("await ShowScheduleDialogAsync();", wrapper, StringComparison.Ordinal);
        Assert.Contains("_viewModel.SelectedEvent is null", guard, StringComparison.Ordinal);
        Assert.Contains("_viewModel.SelectEvent(editingEvent, selectEventDay: false);", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveScheduleEditorCalendarId_NewOrMissingCalendar_UsesRegisteredSelectedCalendar()
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
