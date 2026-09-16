using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class NoOpEditRegressionTests
{
    [Fact]
    public async Task SaveCurrentEventAsync_UnchangedSyncedSchedule_DoesNotDirtyOrRewriteEvent()
    {
        var directory = CreateTempDirectory();
        var repository = new CalendarRepository(Path.Combine(directory, "calendar.db"));
        try
        {
            await repository.InitializeAsync();
            var original = new CalendarEvent
            {
                Id = "schedule-no-op",
                GoogleEventId = "google-schedule-no-op",
                LastSyncedGoogleEtag = "etag-1",
                CalendarId = "primary",
                Title = "unchanged schedule",
                Description = "copied text\r\nsecond line",
                Location = "meeting room",
                Start = new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.FromHours(9)),
                End = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.FromHours(9)),
                StartTimeZoneId = "Asia/Tokyo",
                EndTimeZoneId = "Asia/Tokyo",
                IsAllDay = false,
                IsDirty = false
            };
            await repository.UpsertSyncedEventAsync(original);
            var before = await repository.FindMasterByIdAsync(original.Id);
            Assert.NotNull(before);

            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            viewModel.SelectedEvent = before;

            await viewModel.SaveCurrentEventAsync();

            var after = await repository.FindMasterByIdAsync(original.Id);
            Assert.NotNull(after);
            Assert.False(after.IsDirty);
            Assert.Equal(before.UpdatedAt, after.UpdatedAt);
            Assert.Equal(before.LastSyncedGoogleEtag, after.LastSyncedGoogleEtag);
        }
        finally
        {
            await repository.BeginMaintenanceAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveTodoAsync_UnchangedSyncedTodo_DoesNotDirtyOrRewriteEvent()
    {
        var directory = CreateTempDirectory();
        var repository = new CalendarRepository(Path.Combine(directory, "calendar.db"));
        try
        {
            await repository.InitializeAsync();
            var dueDate = new DateTime(2026, 9, 15);
            var original = new CalendarEvent
            {
                Id = "todo-no-op",
                GoogleEventId = "google-todo-no-op",
                LastSyncedGoogleEtag = "etag-1",
                CalendarId = "primary",
                Title = "unchanged todo",
                Description = TagService.UpdateTodoMarker("body", "A", 20),
                Start = new DateTimeOffset(dueDate),
                End = new DateTimeOffset(dueDate.AddDays(1)),
                IsAllDay = true,
                IsTodoLike = true,
                IsDirty = false
            };
            TodoReminderPolicy.NormalizeLocalFields(original);
            await repository.UpsertSyncedEventAsync(original);
            var before = await repository.FindMasterByIdAsync(original.Id);
            Assert.NotNull(before);

            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            viewModel.EditorCalendarId = before.CalendarId;
            viewModel.EditorColorId = before.ColorId;

            await viewModel.SaveTodoAsync(before, dueDate, "A", 20, before.Title, "body");

            var after = await repository.FindMasterByIdAsync(original.Id);
            Assert.NotNull(after);
            Assert.False(after.IsDirty);
            Assert.Equal(before.UpdatedAt, after.UpdatedAt);
            Assert.Equal(before.LastSyncedGoogleEtag, after.LastSyncedGoogleEtag);
        }
        finally
        {
            await repository.BeginMaintenanceAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"no-op-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
