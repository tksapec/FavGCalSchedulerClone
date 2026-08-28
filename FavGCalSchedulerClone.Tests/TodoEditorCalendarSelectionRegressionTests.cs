using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
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
    public void NewTodo_RegisteredButUnselectedTransientEditorCalendar_UsesSelectedCalendar()
    {
        var calendars = new[]
        {
            new GoogleCalendarSelectionItem { Id = "calendar-a", Summary = "A", IsSelected = true },
            new GoogleCalendarSelectionItem { Id = "calendar-b", Summary = "B", IsSelected = false }
        };
        var request = CreateRequest(true, "calendar-b", calendars);

        var selection = TodoEditorDialog.ResolveCalendarSelection(request);

        Assert.Equal("calendar-a", selection.CalendarId);
        Assert.True(Assert.Single(selection.Options, item => item.Id == "calendar-a").IsSelected);
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

    [Fact]
    public async Task ExistingTodo_PrimaryAliasSavePreservesCalendarAndGoogleIdentity()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveSettingsAsync(new AppSettings
        {
            VisibleCalendarIds = ["calendar-a"],
            ActiveCalendarId = "calendar-a"
        });
        var original = new CalendarEvent
        {
            Id = "todo-primary",
            CalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
            GoogleEventId = "google-todo-primary",
            LastSyncedGoogleEtag = "etag-primary",
            LastSyncedAt = DateTimeOffset.Now.AddDays(-1),
            Title = "Original todo",
            Description = "#todoA10% body",
            Start = new DateTimeOffset(DateTime.Today),
            End = new DateTimeOffset(DateTime.Today.AddDays(1)),
            IsAllDay = true,
            IsTodoLike = true,
            IsDirty = false
        };
        await repository.SaveEventAsync(original);
        var stored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        viewModel.SelectedEvent = stored;
        viewModel.EditorCalendarId = GoogleCalendarDefaults.PrimaryCalendarId;

        await viewModel.SaveTodoAsync(stored, DateTime.Today, "A", 20, "Edited todo", "body");

        var edited = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
        Assert.Equal(GoogleCalendarDefaults.PrimaryCalendarId, edited.CalendarId);
        Assert.Equal("google-todo-primary", edited.GoogleEventId);
        Assert.Equal("Edited todo", edited.Title);
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
