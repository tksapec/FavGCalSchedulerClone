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

        var result = await viewModel.BulkUpdateEventsAsync(
            ["no-op"],
            new BulkEventUpdateRequest(ColorId: "5", UpdateColor: true));

        Assert.Equal(0, result.AffectedCount);
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

        var result = await viewModel.BulkUpdateEventsAsync(
            ["todo-reminder"],
            new BulkEventUpdateRequest(
                ReminderMinutesBeforeStart: 15,
                AppReminderEnabled: true,
                GoogleEmailReminderEnabled: true));

        Assert.Equal(0, result.AffectedCount);
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

        var result = await viewModel.BulkUpdateEventsAsync(
            ["schedule-reminder", "todo-reminder-mixed"],
            new BulkEventUpdateRequest(
                ReminderMinutesBeforeStart: 30,
                AppReminderEnabled: true,
                GoogleEmailReminderEnabled: false));

        Assert.Equal(1, result.AffectedCount);
        var schedule = await repository.FindMasterByIdAsync("schedule-reminder");
        var todo = await repository.FindMasterByIdAsync("todo-reminder-mixed");
        Assert.NotNull(schedule);
        Assert.NotNull(todo);
        Assert.Equal(30, schedule!.ReminderMinutesBeforeStart);
        Assert.True(schedule.IsDirty);
        Assert.Null(todo!.ReminderMinutesBeforeStart);
        Assert.False(todo.IsDirty);
    }

    [Fact]
    public async Task BulkUpdateEventsDetailedAsync_ReportsGeneratedOccurrenceAndMissingTarget()
    {
        var (viewModel, repository) = await CreateViewModelAsync();
        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "series",
            CalendarId = "primary",
            GoogleEventId = "google-series",
            Title = "Recurring master",
            RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=2\"]",
            Start = new DateTimeOffset(DateTime.Today.AddHours(8)),
            End = new DateTimeOffset(DateTime.Today.AddHours(9))
        });
        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "normal-target",
            CalendarId = "primary",
            GoogleEventId = "google-normal-target",
            Title = "Normal",
            Start = new DateTimeOffset(DateTime.Today.AddHours(10)),
            End = new DateTimeOffset(DateTime.Today.AddHours(11))
        });
        var occurrenceId = $"series@{new DateTimeOffset(DateTime.Today.AddDays(1).AddHours(8)).UtcTicks}";

        var result = await viewModel.BulkUpdateEventsDetailedAsync(
            ["normal-target", occurrenceId, "missing-target"],
            new BulkEventUpdateRequest(ColorId: "6", UpdateColor: true));

        Assert.Equal(3, result.SelectedCount);
        Assert.Equal(1, result.AffectedCount);
        Assert.Equal(1, result.UnsupportedRecurrenceCount);
        Assert.Equal(1, result.MissingCount);
        Assert.Equal(0, result.TodoReminderSkippedCount);
        var normal = await repository.FindMasterByIdAsync("normal-target");
        var master = await repository.FindMasterByIdAsync("series");
        Assert.Equal("6", normal!.ColorId);
        Assert.True(normal.IsDirty);
        Assert.Null(master!.ColorId);
        Assert.False(master.IsDirty);
    }

    [Fact]
    public async Task BulkUpdateEventsDetailedAsync_ReportsTodoReminderSkipped()
    {
        var (viewModel, repository) = await CreateViewModelAsync();
        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "schedule-detailed",
            CalendarId = "primary",
            GoogleEventId = "google-schedule-detailed",
            Title = "Schedule",
            Start = new DateTimeOffset(DateTime.Today.AddHours(9)),
            End = new DateTimeOffset(DateTime.Today.AddHours(10))
        });
        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "todo-detailed",
            CalendarId = "primary",
            GoogleEventId = "google-todo-detailed",
            Title = "Todo detailed",
            Description = "#todoC0%",
            Start = new DateTimeOffset(DateTime.Today),
            End = new DateTimeOffset(DateTime.Today.AddDays(1)),
            IsAllDay = true
        });

        var result = await viewModel.BulkUpdateEventsDetailedAsync(
            ["schedule-detailed", "todo-detailed"],
            new BulkEventUpdateRequest(
                ReminderMinutesBeforeStart: 20,
                AppReminderEnabled: true,
                GoogleEmailReminderEnabled: false));

        Assert.Equal(2, result.SelectedCount);
        Assert.Equal(1, result.AffectedCount);
        Assert.Equal(1, result.TodoReminderSkippedCount);
        Assert.Equal(0, result.UnsupportedRecurrenceCount);
        Assert.Equal(0, result.MissingCount);
    }

    [Fact]
    public async Task BulkDeleteEventsDetailedAsync_ReportsGeneratedOccurrenceAndMissingTarget()
    {
        var (viewModel, repository) = await CreateViewModelAsync();
        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "delete-series",
            CalendarId = "primary",
            GoogleEventId = "google-delete-series",
            Title = "Recurring master",
            RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=2\"]",
            Start = new DateTimeOffset(DateTime.Today.AddHours(8)),
            End = new DateTimeOffset(DateTime.Today.AddHours(9))
        });
        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "delete-normal",
            CalendarId = "primary",
            GoogleEventId = "google-delete-normal",
            Title = "Delete normal",
            Start = new DateTimeOffset(DateTime.Today.AddHours(12)),
            End = new DateTimeOffset(DateTime.Today.AddHours(13))
        });
        var occurrenceId = $"delete-series@{new DateTimeOffset(DateTime.Today.AddDays(1).AddHours(8)).UtcTicks}";

        var result = await viewModel.BulkDeleteEventsDetailedAsync(
            ["delete-normal", occurrenceId, "delete-missing"]);

        Assert.Equal(3, result.SelectedCount);
        Assert.Equal(1, result.AffectedCount);
        Assert.Equal(1, result.UnsupportedRecurrenceCount);
        Assert.Equal(1, result.MissingCount);
        var normal = await repository.FindMasterByIdAsync("delete-normal");
        var master = await repository.FindMasterByIdAsync("delete-series");
        Assert.True(normal!.IsDeleted);
        Assert.True(normal.IsDirty);
        Assert.False(master!.IsDeleted);
        Assert.False(master.IsDirty);
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
