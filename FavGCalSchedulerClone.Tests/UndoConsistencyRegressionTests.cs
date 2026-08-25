using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class UndoConsistencyRegressionTests
{
    [Fact]
    public async Task NewSchedule_BecomesTheLatestUndoOperation()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await repository.SaveEventAsync(CreateEvent("baseline", "Baseline", new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.FromHours(9))));
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();

            await viewModel.BulkUpdateEventsAsync(["baseline"], new BulkEventUpdateRequest(ColorId: "5"));
            Assert.Equal("一括編集", viewModel.UndoStatusText);

            viewModel.BeginNewEvent(new DateTime(2026, 8, 26), new DateTime(2026, 8, 26, 8, 0, 0));
            viewModel.Title = "New schedule";
            await viewModel.SaveCurrentEventAsync();

            Assert.Equal("予定追加", viewModel.UndoStatusText);
            var created = Assert.Single(await LoadRangeAsync(repository), item => item.Title == "New schedule" && !item.IsDeleted);

            Assert.True(await viewModel.UndoLastChangeAsync());

            Assert.Null(await repository.FindEventByIdAsync(created.Id));
            Assert.Equal("5", (await repository.FindEventByIdAsync("baseline"))?.ColorId);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task ReminderTestSchedule_BecomesTheLatestUndoOperation()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();

            var created = await viewModel.CreateTwoMinuteReminderTestEventAsync();

            Assert.Equal("予定追加", viewModel.UndoStatusText);
            Assert.NotNull(await repository.FindEventByIdAsync(created.Id));
            Assert.True(await viewModel.UndoLastChangeAsync());
            Assert.Null(await repository.FindEventByIdAsync(created.Id));
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task NewTodo_BecomesTheLatestUndoOperation()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();

            await viewModel.SaveTodoAsync(new DateTime(2026, 8, 27), "A", 0, "New todo", null);

            Assert.Equal("ToDo追加", viewModel.UndoStatusText);
            var created = Assert.Single(await LoadRangeAsync(repository), item => item.Title == "New todo" && !item.IsDeleted);
            Assert.True(await viewModel.UndoLastChangeAsync());
            Assert.Null(await repository.FindEventByIdAsync(created.Id));
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task FailedTodoEdit_DoesNotReplacePriorUndoOrMutateTheSourceObject()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await repository.SaveEventAsync(CreateEvent("baseline", "Baseline", new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.FromHours(9))));
            var todo = CreateEvent("todo-fail", "Original todo", new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.FromHours(9)));
            todo.End = todo.Start.AddDays(1);
            todo.IsAllDay = true;
            todo.IsTodoLike = true;
            todo.Description = TagService.UpdateTodoMarker(null, "A", 0);
            await repository.SaveEventAsync(todo);

            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            await viewModel.BulkUpdateEventsAsync(["baseline"], new BulkEventUpdateRequest(ColorId: "5"));
            Assert.Equal("一括編集", viewModel.UndoStatusText);

            var editingTodo = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync("todo-fail"));
            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                await using var trigger = connection.CreateCommand();
                trigger.CommandText = """
                    CREATE TRIGGER fail_todo_edit
                    BEFORE INSERT ON events
                    WHEN NEW.id = 'todo-fail'
                    BEGIN
                        SELECT RAISE(ABORT, 'intentional todo edit failure');
                    END;
                    """;
                await trigger.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAsync<SqliteException>(() =>
                viewModel.SaveTodoAsync(editingTodo, new DateTime(2026, 8, 29), "B", 40, "Mutated todo", "changed"));

            Assert.Equal("一括編集", viewModel.UndoStatusText);
            Assert.Equal("Original todo", editingTodo.Title);
            Assert.Equal("Original todo", (await repository.FindEventByIdAsync("todo-fail"))?.Title);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task CopyPaste_BecomesUndoableAndUndoRemovesOnlyThePastedEvent()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await repository.SaveEventAsync(CreateEvent("source", "Source", new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.FromHours(9))));
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            var source = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync("source"));
            viewModel.SelectEvent(source);
            viewModel.CopySelectedEventLabel();

            Assert.True(await viewModel.PasteEventLabelAsync(new DateTime(2026, 8, 30)));
            var pastedId = Assert.IsType<CalendarEvent>(viewModel.SelectedEvent).Id;
            Assert.NotEqual(source.Id, pastedId);
            Assert.Equal("予定貼り付け", viewModel.UndoStatusText);

            Assert.True(await viewModel.UndoLastChangeAsync());

            Assert.Null(await repository.FindEventByIdAsync(pastedId));
            Assert.NotNull(await repository.FindEventByIdAsync(source.Id));
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task CutPaste_UndoRestoresSourceAndRemovesPastedEvent()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await repository.SaveEventAsync(CreateEvent("source-cut", "Source cut", new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.FromHours(9))));
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            var source = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync("source-cut"));
            viewModel.SelectEvent(source);
            viewModel.CutSelectedEventLabel();

            Assert.True(await viewModel.PasteEventLabelAsync(new DateTime(2026, 8, 31)));
            var pastedId = Assert.IsType<CalendarEvent>(viewModel.SelectedEvent).Id;
            Assert.True((await repository.FindEventByIdAsync(source.Id))?.IsDeleted);
            Assert.Equal("予定移動", viewModel.UndoStatusText);

            Assert.True(await viewModel.UndoLastChangeAsync());

            var restoredSource = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(source.Id));
            Assert.False(restoredSource.IsDeleted);
            Assert.Null(await repository.FindEventByIdAsync(pastedId));
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    private static CalendarEvent CreateEvent(string id, string title, DateTimeOffset start) => new()
    {
        Id = id,
        CalendarId = "primary",
        Title = title,
        Start = start,
        End = start.AddHours(1),
        IsDirty = true
    };

    private static Task<IReadOnlyList<CalendarEvent>> LoadRangeAsync(CalendarRepository repository) =>
        repository.LoadEventsAsync(
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.FromHours(9)),
            includeDeleted: true);

    private static void CleanupDatabase(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        DeleteIfExists(dbPath);
        DeleteIfExists(dbPath + "-wal");
        DeleteIfExists(dbPath + "-shm");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
