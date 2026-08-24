using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class TimeZoneEditorRegressionTests
{
    [Fact]
    public async Task SaveCurrentEventAsync_ChangedWallClockUsesOriginalEventTimeZone()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var original = new CalendarEvent
        {
            Id = "ny-edit",
            CalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
            GoogleEventId = "remote-ny-edit",
            Title = "New York meeting",
            Start = new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.FromHours(-4)),
            End = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.FromHours(-4)),
            StartTimeZoneId = "America/New_York",
            EndTimeZoneId = "America/New_York",
            IsDirty = false
        };
        await repository.SaveEventAsync(original);
        var stored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        viewModel.SelectedEvent = stored;
        viewModel.StartTime = "10:00";
        viewModel.EndTime = "11:00";

        await viewModel.SaveCurrentEventAsync();

        var edited = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
        Assert.Equal(new TimeSpan(-4, 0, 0), edited.Start.Offset);
        Assert.Equal(new TimeSpan(-4, 0, 0), edited.End.Offset);
        Assert.Equal(10, edited.Start.Hour);
        Assert.Equal(11, edited.End.Hour);
        Assert.Equal("America/New_York", edited.StartTimeZoneId);
        Assert.Equal("America/New_York", edited.EndTimeZoneId);
    }
}
