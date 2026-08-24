using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class RecurrenceScopeTimeZoneRegressionTests
{
    [Fact]
    public async Task ThisAndFollowing_StartsFutureSeriesAtEditedSplitOccurrence()
    {
        var repository = await CreateRepositoryAsync();
        var master = new CalendarEvent
        {
            Id = "split-anchor-series",
            CalendarId = "primary",
            Title = "Split anchor",
            Start = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 5, 10, 10, 0, 0, TimeSpan.FromHours(9)),
            RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=5\"]"
        };
        await repository.SaveEventAsync(master);
        var viewModel = await CreateViewModelAsync(repository);
        var occurrence = await SelectOccurrenceAsync(viewModel, new DateTime(2026, 5, 12), master.Title);
        PopulateEditor(viewModel, occurrence, "09:00", "10:00");

        await viewModel.SaveCurrentEventAsync(RecurrenceEditScope.ThisAndFollowing);

        var events = await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(9)),
            includeDeleted: true);
        var future = Assert.Single(events, item => item.Id != master.Id && item.IsRecurringMaster && !item.IsDeleted);
        Assert.Equal(new DateTime(2026, 5, 12), future.Start.Date);
        Assert.Equal(new TimeSpan(9, 0, 0), future.Start.TimeOfDay);
        Assert.Contains("COUNT=3", future.RecurrenceJson ?? "");
    }

    [Fact]
    public async Task AllEvents_ReevaluatesEditedWallClockInMasterTimeZoneAcrossDst()
    {
        var repository = await CreateRepositoryAsync();
        var master = new CalendarEvent
        {
            Id = "dst-all-events-series",
            CalendarId = "primary",
            Title = "New York weekly",
            Start = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.FromHours(-5)),
            End = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.FromHours(-5)),
            StartTimeZoneId = "America/New_York",
            EndTimeZoneId = "America/New_York",
            RecurrenceJson = "[\"RRULE:FREQ=WEEKLY;COUNT=30\"]"
        };
        await repository.SaveEventAsync(master);
        var viewModel = await CreateViewModelAsync(repository);
        var occurrence = await SelectOccurrenceAsync(viewModel, new DateTime(2026, 7, 6), master.Title);
        Assert.Equal(TimeSpan.FromHours(-4), occurrence.Start.Offset);
        PopulateEditor(viewModel, occurrence, "10:00", "11:00");

        await viewModel.SaveCurrentEventAsync(RecurrenceEditScope.AllEvents);

        var stored = Assert.IsType<CalendarEvent>(await repository.FindMasterByIdAsync(master.Id));
        Assert.Equal(new DateTime(2026, 1, 5), stored.Start.Date);
        Assert.Equal(new TimeSpan(10, 0, 0), stored.Start.TimeOfDay);
        Assert.Equal(TimeSpan.FromHours(-5), stored.Start.Offset);
        Assert.Equal(TimeSpan.FromHours(-5), stored.End.Offset);
        Assert.Equal("America/New_York", stored.StartTimeZoneId);
        Assert.Equal("America/New_York", stored.EndTimeZoneId);
    }

    private static async Task<CalendarRepository> CreateRepositoryAsync()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        return repository;
    }

    private static async Task<MainViewModel> CreateViewModelAsync(CalendarRepository repository)
    {
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static async Task<CalendarEvent> SelectOccurrenceAsync(MainViewModel viewModel, DateTime date, string title)
    {
        await viewModel.NavigateToDateAsync(date);
        var occurrence = viewModel.CalendarDays
            .Single(day => day.Date == date.Date)
            .Segments
            .Single(segment => segment.Event?.Title == title)
            .Event!;
        viewModel.SelectEvent(occurrence);
        return occurrence;
    }

    private static void PopulateEditor(MainViewModel viewModel, CalendarEvent occurrence, string startTime, string endTime)
    {
        viewModel.Title = occurrence.Title;
        viewModel.Description = occurrence.Description ?? "";
        viewModel.Location = occurrence.Location ?? "";
        viewModel.StartDate = occurrence.Start.Date;
        viewModel.EndDate = occurrence.End.Date;
        viewModel.StartTime = startTime;
        viewModel.EndTime = endTime;
        viewModel.IsAllDay = false;
    }
}
