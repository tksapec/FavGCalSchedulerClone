using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class EventEditorCalendarIdentityRegressionTests
{
    [Fact]
    public async Task SaveCurrentEventAsync_SourceCalendarMissingFromList_DoesNotSilentlyMoveEvent()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        await repository.SaveSettingsAsync(new AppSettings
        {
            VisibleCalendarIds = ["other-calendar"],
            ActiveCalendarId = "other-calendar"
        });
        var original = new CalendarEvent
        {
            Id = "linked-hidden-calendar",
            CalendarId = "source-calendar",
            GoogleEventId = "remote-source-event",
            LastSyncedGoogleEtag = "etag-source",
            LastSyncedAt = DateTimeOffset.Now.AddDays(-1),
            Title = "Original title",
            Start = new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.FromHours(9)),
            IsDirty = false
        };
        await repository.SaveEventAsync(original);
        var stored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        Assert.DoesNotContain(viewModel.AvailableCalendars, item => item.Id == original.CalendarId);
        viewModel.SelectedEvent = stored;
        viewModel.Title = "Edited title";

        await viewModel.SaveCurrentEventAsync();

        var edited = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
        Assert.Equal("source-calendar", edited.CalendarId);
        Assert.Equal("remote-source-event", edited.GoogleEventId);
        Assert.Equal("Edited title", edited.Title);
        var dirty = await repository.LoadDirtyEventsAsync();
        Assert.Single(dirty);
        Assert.Equal(original.Id, dirty[0].Id);
    }
}
