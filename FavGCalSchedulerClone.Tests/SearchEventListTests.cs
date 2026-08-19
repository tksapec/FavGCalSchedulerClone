using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class SearchEventListTests
{
    [Fact]
    public async Task SearchEventsAsync_FiltersSchedulesAndTodos()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        await repository.SaveEventAsync(Event("schedule", isTodo: false));
        await repository.SaveEventAsync(Event("todo", isTodo: true));
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));

        var all = await viewModel.SearchEventsAsync(new EventListFilter("", EventKindFilter.All, EventSearchRange.Year, new DateTime(2026, 1, 1)));
        var schedules = await viewModel.SearchEventsAsync(new EventListFilter("", EventKindFilter.Schedule, EventSearchRange.Year, new DateTime(2026, 1, 1)));
        var todos = await viewModel.SearchEventsAsync(new EventListFilter("", EventKindFilter.Todo, EventSearchRange.Year, new DateTime(2026, 1, 1)));

        Assert.Equal(2, all.Count);
        Assert.Single(schedules);
        Assert.Equal("schedule", schedules[0].Title);
        Assert.Single(todos);
        Assert.Equal("todo", todos[0].Title);
    }

    [Fact]
    public async Task SearchEventsAsync_YearRangeKeepsExistingListScope()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        await repository.SaveEventAsync(Event("this year", isTodo: false, start: new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)));
        await repository.SaveEventAsync(Event("next year", isTodo: false, start: new DateTimeOffset(2027, 5, 1, 9, 0, 0, TimeSpan.Zero)));
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));

        var result = await viewModel.SearchEventsAsync(new EventListFilter("", EventKindFilter.All, EventSearchRange.Year, new DateTime(2026, 5, 31)));

        Assert.Single(result);
        Assert.Equal("this year", result[0].Title);
    }

    [Fact]
    public async Task SearchEventsAsync_CustomRangeUsesSelectedStartAndEndDates()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        await repository.SaveEventAsync(Event("inside", isTodo: false, start: new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.Zero)));
        await repository.SaveEventAsync(Event("outside", isTodo: false, start: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)));
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));

        var result = await viewModel.SearchEventsAsync(new EventListFilter(
            "",
            EventKindFilter.All,
            EventSearchRange.Custom,
            new DateTime(2026, 5, 1),
            StartDate: new DateTime(2026, 5, 10),
            EndDate: new DateTime(2026, 5, 20)));

        Assert.Single(result);
        Assert.Equal("inside", result[0].Title);
    }

    [Fact]
    public void EventListDialog_ResultColumnsMatchSearchListRequirements()
    {
        Assert.Equal(
            ["開始日時", "終了日時", "アラーム", "カレンダー", "場所", "件名", "内容", "概要"],
            EventListDialog.ResultColumnHeaders);
    }

    private static CalendarEvent Event(string title, bool isTodo, DateTimeOffset? start = null)
    {
        var eventStart = start ?? new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero);
        return new CalendarEvent
        {
            CalendarId = "primary",
            Title = title,
            Description = isTodo ? "#todoA50% body" : "body",
            Start = eventStart,
            End = eventStart.AddHours(1),
            IsAllDay = false
        };
    }
}
