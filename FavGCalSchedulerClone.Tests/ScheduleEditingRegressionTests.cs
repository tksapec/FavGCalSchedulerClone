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
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        var handler = ExtractMethod(source, "private async void SelectedDayEventsGrid_MouseDoubleClick", "private async void TodoEventsGrid_MouseDoubleClick");
        var fastEditor = ExtractMethod(source, "private async Task OpenSidePanelEventEditorAsync", "private async Task OpenGridEventEditorAsync");

        Assert.Contains("await OpenSidePanelEventEditorAsync(calendarEvent);", handler, StringComparison.Ordinal);
        Assert.Contains("_viewModel.SelectEvent(calendarEvent, selectEventDay: false);", fastEditor, StringComparison.Ordinal);
        Assert.Contains("await ShowScheduleDialogAsync();", fastEditor, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateToDateAsync", fastEditor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduleEditor_RestoresCapturedEditingEventBeforeApplyingDialogResult()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        var method = ExtractMethod(source, "private async Task ShowScheduleDialogAsync", "private async Task ShowTodoDialogAsync");

        var restore = method.IndexOf("_viewModel.SelectEvent(editingEvent, selectEventDay: false);", StringComparison.Ordinal);
        var apply = method.IndexOf("_viewModel.EditorCalendarId = result.CalendarId;", StringComparison.Ordinal);
        var save = method.IndexOf("await _viewModel.SaveCurrentEventAsync(recurrenceScope);", StringComparison.Ordinal);

        Assert.True(restore >= 0, "The captured editing event must be restored before saving.");
        Assert.True(apply > restore, "Dialog values must be applied after restoring the editing identity.");
        Assert.True(save > apply, "The restored editing identity and dialog values must be in place before saving.");
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
        Assert.Equal("hidden-calendar", viewModel.ResolveScheduleEditorCalendarId(new CalendarEvent { CalendarId = "hidden-calendar" }));
    }

    private static string ExtractMethod(string source, string startMarker, string nextMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{startMarker} was not found.");
        var end = source.IndexOf(nextMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"{nextMarker} was not found after {startMarker}.");
        return source[start..end];
    }
}
