using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class BulkEventOperationRegressionTests
{
    [Fact]
    public async Task BulkUpdateEventsAsync_NoOpDoesNotDirtyOrCountEvent()
    {
        var (viewModel, repository) = await CreateViewModelAsync();
        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "no-op",
            CalendarId = "primary",
            GoogleEventId = "google-no-op",
            Title = "Already yellow",
            ColorId = "5",
            Start = new DateTimeOffset(DateTime.Today.AddHours(9)),
            End = new DateTimeOffset(DateTime.Today.AddHours(10))
        });

        var updated = await viewModel.BulkUpdateEventsAsync(
            ["no-op"],
            new BulkEventUpdateRequest(ColorId: "5", UpdateColor: true));

        Assert.Equal(0, updated);
        var stored = await repository.FindMasterByIdAsync("no-op");
        Assert.NotNull(stored);
        Assert.Equal("5", stored!.ColorId);
        Assert.False(stored.IsDirty);
        Assert.Null(stored.DirtyFields);
    }

    [Fact]
    public async Task BulkUpdateEventsAsync_ReminderChangeSkipsTodoAndLeavesItClean()
    {
        var (viewModel, repository) = await CreateViewModelAsync();
        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "todo-reminder",
            CalendarId = "primary",
            GoogleEventId = "google-todo-reminder",
            Title = "Todo",
            Description = "#todoA0%",
            Start = new DateTimeOffset(DateTime.Today),
            End = new DateTimeOffset(DateTime.Today.AddDays(1)),
            IsAllDay = true
        });

        var updated = await viewModel.BulkUpdateEventsAsync(
            ["todo-reminder"],
            new BulkEventUpdateRequest(
                ReminderMinutesBeforeStart: 15,
                AppReminderEnabled: true,
                GoogleEmailReminderEnabled: true));

        Assert.Equal(0, updated);
        var stored = await repository.FindMasterByIdAsync("todo-reminder");
        Assert.NotNull(stored);
        Assert.True(stored!.IsTodoLike);
        Assert.Null(stored.ReminderMinutesBeforeStart);
        Assert.Empty(stored.AppReminderMinutesBeforeStart);
        Assert.Empty(stored.GoogleEmailReminderMinutesBeforeStart);
        Assert.False(stored.IsAppReminderEnabled);
        Assert.False(stored.IsGoogleEmailReminderEnabled);
        Assert.False(stored.IsDirty);
        Assert.Null(stored.DirtyFields);
    }

    [Fact]
    public async Task BulkUpdateEventsAsync_MixedScheduleAndTodoAppliesReminderOnlyToSchedule()
    {
        var (viewModel, repository) = await CreateViewModelAsync();
        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "schedule-reminder",
            CalendarId = "primary",
            GoogleEventId = "google-schedule-reminder",
            Title = "Schedule",
            Start = new DateTimeOffset(DateTime.Today.AddHours(9)),
            End = new DateTimeOffset(DateTime.Today.AddHours(10))
        });
        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "todo-reminder-mixed",
            CalendarId = "primary",
            GoogleEventId = "google-todo-reminder-mixed",
            Title = "Todo mixed",
            Description = "#todoB0%",
            Start = new DateTimeOffset(DateTime.Today),
            End = new DateTimeOffset(DateTime.Today.AddDays(1)),
            IsAllDay = true
        });

        var updated = await viewModel.BulkUpdateEventsAsync(
            ["schedule-reminder", "todo-reminder-mixed"],
            new BulkEventUpdateRequest(
                ReminderMinutesBeforeStart: 30,
                AppReminderEnabled: true,
                GoogleEmailReminderEnabled: false));

        Assert.Equal(1, updated);
        var schedule = await repository.FindMasterByIdAsync("schedule-reminder");
        var todo = await repository.FindMasterByIdAsync("todo-reminder-mixed");
        Assert.NotNull(schedule);
        Assert.NotNull(todo);
        Assert.Equal(30, schedule!.ReminderMinutesBeforeStart);
        Assert.True(schedule.IsDirty);
        Assert.Null(todo!.ReminderMinutesBeforeStart);
        Assert.False(todo.IsDirty);
    }

    private static async Task<(MainViewModel ViewModel, CalendarRepository Repository)> CreateViewModelAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        return (viewModel, repository);
    }
}
