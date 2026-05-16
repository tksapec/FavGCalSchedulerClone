using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class MainViewModelTodoTests
{
    [Fact]
    public async Task SaveTodoAsync_SplitsIncompleteAndCompletedTodos()
    {
        var viewModel = await CreateViewModelAsync();
        var dueDate = DateTime.Today;

        await viewModel.SaveTodoAsync(dueDate, "A", 56, "未処理ToDo", "本文");
        await viewModel.SaveTodoAsync(dueDate, "B", 100, "処理済みToDo", "本文");

        Assert.Single(viewModel.TodoEvents);
        Assert.Single(viewModel.CompletedTodoEvents);
        Assert.Equal("A", viewModel.TodoEvents[0].TodoPriority);
        Assert.Equal(56, viewModel.TodoEvents[0].TodoProgress);
        Assert.True(viewModel.CompletedTodoEvents[0].IsTodoDone);
    }

    [Fact]
    public async Task MarkSelectedTodoDoneAsync_MovesTodoToCompletedCollection()
    {
        var viewModel = await CreateViewModelAsync();
        await viewModel.SaveTodoAsync(DateTime.Today, "A", 56, "確認", "本文 #todoB10% 詳細");

        viewModel.SelectedEvent = viewModel.TodoEvents.Single();
        await viewModel.MarkSelectedTodoDoneAsync();

        Assert.Empty(viewModel.TodoEvents);
        Assert.Single(viewModel.CompletedTodoEvents);
        Assert.Contains("#todoA100%", viewModel.CompletedTodoEvents[0].Description);
        Assert.DoesNotContain("#todoB10%", viewModel.CompletedTodoEvents[0].Description);
    }

    private static async Task<MainViewModel> CreateViewModelAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        viewModel.CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return viewModel;
    }
}
