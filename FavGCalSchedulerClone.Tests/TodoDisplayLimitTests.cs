using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class TodoDisplayLimitTests
{
    [Fact]
    public async Task TodoLists_ShowRemainingCountAndCanShowAllItems()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(databasePath);
        await repository.InitializeAsync();

        for (var index = 0; index < 103; index++)
        {
            await repository.SaveEventAsync(CreateTodo($"incomplete-{index:D3}", index, done: false));
        }

        for (var index = 0; index < 102; index++)
        {
            await repository.SaveEventAsync(CreateTodo($"completed-{index:D3}", index, done: true));
        }

        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();

        Assert.Equal(100, viewModel.TodoEvents.Count);
        Assert.Equal(3, viewModel.TodoEventsRemainingCount);
        Assert.Contains("103", viewModel.TodoEventsCountText, StringComparison.Ordinal);
        Assert.True(viewModel.IsTodoEventsLimitControlVisible);

        await viewModel.ToggleIncompleteTodoListLimitAsync();
        Assert.Equal(103, viewModel.TodoEvents.Count);
        Assert.Equal(0, viewModel.TodoEventsRemainingCount);
        Assert.Equal("100件に戻す", viewModel.TodoEventsLimitButtonText);

        Assert.Equal(100, viewModel.CompletedTodoEvents.Count);
        Assert.Equal(2, viewModel.CompletedTodoEventsRemainingCount);
        await viewModel.ToggleCompletedTodoListLimitAsync();
        Assert.Equal(102, viewModel.CompletedTodoEvents.Count);
        Assert.Equal(0, viewModel.CompletedTodoEventsRemainingCount);
    }

    private static CalendarEvent CreateTodo(string id, int index, bool done)
    {
        var dueDate = DateTime.Today.AddDays(index % 3);
        return new CalendarEvent
        {
            Id = id,
            CalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
            Title = id,
            Description = done ? "#todoA100%" : "#todoA0%",
            Start = new DateTimeOffset(dueDate),
            End = new DateTimeOffset(dueDate.AddDays(1)),
            IsAllDay = true,
            IsTodoLike = true
        };
    }
}
