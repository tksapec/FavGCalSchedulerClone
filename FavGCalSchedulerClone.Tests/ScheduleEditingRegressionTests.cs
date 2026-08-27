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
        var source = await ReadMainWindowSourceAsync();
        var handler = ExtractMethod(
            source,
            "private async void SelectedDayEventsGrid_MouseDoubleClick",
            "private void Window_PreviewKeyDown");

        Assert.Contains("if (calendarEvent.IsTodoLike)", handler, StringComparison.Ordinal);
        Assert.Contains("_viewModel.SelectEvent(calendarEvent, selectEventDay: false);", handler, StringComparison.Ordinal);
        Assert.Contains("await ShowScheduleDialogAsync();", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateToDateAsync", handler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduleEditor_RestoresCapturedEditingIdentityBeforeApplyingResult()
    {
        var source = await ReadMainWindowSourceAsync();
        var method = ExtractMethod(
            source,
            "private async Task ShowScheduleDialogAsync",
            "private async Task ShowSelectedTodoDialogAsync");

        var restore = method.IndexOf("_viewModel.SelectEvent(editingEvent, selectEventDay: false);", StringComparison.Ordinal);
        var apply = method.IndexOf("_viewModel.EditorCalendarId =", StringComparison.Ordinal);
        var save = method.IndexOf("await _viewModel.SaveCurrentEventAsync(recurrenceScope);", StringComparison.Ordinal);

        Assert.True(restore >= 0, "The captured event identity must be restored when the selection was lost or replaced.");
        Assert.True(apply > restore, "The editing identity must be restored before applying dialog values.");
        Assert.True(save > apply, "Saving must happen after identity restoration and dialog-value application.");
    }

    [Fact]
    public async Task ScheduleEditor_UsesResolvedCalendarAndInMemoryHistoryBeforeShowingDialog()
    {
        var source = await ReadMainWindowSourceAsync();
        var method = ExtractMethod(
            source,
            "private async Task ShowScheduleDialogAsync",
            "private async Task ShowSelectedTodoDialogAsync");

        Assert.Contains("_viewModel.ResolveScheduleEditorCalendarId(editingEvent)", method, StringComparison.Ordinal);
        Assert.Contains("_viewModel.CreateScheduleEditorCalendarOptions(editingEvent, editorCalendarId)", method, StringComparison.Ordinal);
        Assert.Contains("_viewModel.ScheduleLocationHistory", method, StringComparison.Ordinal);
        Assert.Contains("_viewModel.ScheduleTitleHistory", method, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadScheduleLocationHistoryAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadScheduleTitleHistoryAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveScheduleEditorCalendarId_NewOrPrimaryAlias_UsesConcreteRegisteredCalendar()
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
    public void ResolveScheduleEditorSavedCalendarId_PrimaryAliasIsPreservedUnlessUserChangesCalendar()
    {
        var viewModel = CreateViewModel();
        var editingEvent = new CalendarEvent { CalendarId = GoogleCalendarDefaults.PrimaryCalendarId };

        Assert.Equal(
            GoogleCalendarDefaults.PrimaryCalendarId,
            viewModel.ResolveScheduleEditorSavedCalendarId(editingEvent, "calendar-a", "calendar-a"));
        Assert.Equal(
            "calendar-b",
            viewModel.ResolveScheduleEditorSavedCalendarId(editingEvent, "calendar-a", "calendar-b"));
        Assert.Equal(
            "calendar-a",
            viewModel.ResolveScheduleEditorSavedCalendarId(null, "calendar-a", "calendar-a"));
    }

    [Fact]
    public void CreateScheduleEditorCalendarOptions_KeepsUnavailableCurrentCalendarSelectable()
    {
        var viewModel = CreateViewModel();
        viewModel.AvailableCalendars.Add(new GoogleCalendarSelectionItem { Id = "calendar-a", Summary = "A", IsSelected = true });
        var editingEvent = new CalendarEvent { CalendarId = "archived-calendar" };

        var options = viewModel.CreateScheduleEditorCalendarOptions(editingEvent, "archived-calendar");

        Assert.Contains(options, item => item.Id == "archived-calendar");
        Assert.Contains(options, item => item.Id == "calendar-a");
        Assert.Equal(2, options.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
    }

    private static MainViewModel CreateViewModel()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        return new MainViewModel(repository, new GoogleCalendarSyncService(repository));
    }

    private static Task<string> ReadMainWindowSourceAsync() =>
        File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.xaml.cs"));

    private static string ExtractMethod(string source, string startMarker, string nextMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{startMarker} was not found.");
        var end = source.IndexOf(nextMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"{nextMarker} was not found after {startMarker}.");
        return source[start..end];
    }
}
