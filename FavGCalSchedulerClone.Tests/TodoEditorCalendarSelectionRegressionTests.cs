using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class TodoEditorCalendarSelectionRegressionTests
{
    [Fact]
    public void NewTodo_PrimaryAliasMissingFromList_UsesSelectedRegisteredCalendar()
    {
        var calendars = new[]
        {
            new GoogleCalendarSelectionItem { Id = "calendar-a", Summary = "A", IsSelected = true },
            new GoogleCalendarSelectionItem { Id = "calendar-b", Summary = "B" }
        };
        var request = CreateRequest(true, GoogleCalendarDefaults.PrimaryCalendarId, calendars);

        var selection = TodoEditorDialog.ResolveCalendarSelection(request);

        Assert.Equal("calendar-a", selection.CalendarId);
        Assert.Equal(2, selection.Options.Count);
        Assert.DoesNotContain(selection.Options, item => item.Id == GoogleCalendarDefaults.PrimaryCalendarId);
    }

    [Fact]
    public void ExistingTodo_PrimaryAliasMissingFromList_RemainsSelectableWithoutCalendarMove()
    {
        var calendars = new[]
        {
            new GoogleCalendarSelectionItem { Id = "calendar-a", Summary = "A", IsSelected = true }
        };
        var request = CreateRequest(false, GoogleCalendarDefaults.PrimaryCalendarId, calendars);

        var selection = TodoEditorDialog.ResolveCalendarSelection(request);

        Assert.Equal(GoogleCalendarDefaults.PrimaryCalendarId, selection.CalendarId);
        Assert.Contains(selection.Options, item =>
            item.Id == GoogleCalendarDefaults.PrimaryCalendarId
            && item.Summary == "メインカレンダー");
    }

    [Fact]
    public void ExistingTodo_UnavailableHistoricalCalendar_RemainsSelectable()
    {
        var calendars = new[]
        {
            new GoogleCalendarSelectionItem { Id = "calendar-a", Summary = "A", IsSelected = true }
        };
        var request = CreateRequest(false, "archived-calendar", calendars);

        var selection = TodoEditorDialog.ResolveCalendarSelection(request);

        Assert.Equal("archived-calendar", selection.CalendarId);
        Assert.Contains(selection.Options, item => item.Id == "archived-calendar");
    }

    private static TodoEditorRequest CreateRequest(
        bool isNew,
        string calendarId,
        IEnumerable<GoogleCalendarSelectionItem> calendars) =>
        new(
            isNew,
            DateTime.Today,
            "A",
            0,
            calendarId,
            null,
            string.Empty,
            string.Empty,
            calendars);
}
