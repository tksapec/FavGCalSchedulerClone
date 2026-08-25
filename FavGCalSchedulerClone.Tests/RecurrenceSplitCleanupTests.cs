using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class RecurrenceSplitCleanupTests
{
    [Fact]
    public async Task ThisAndFollowingSplit_RetiresOriginalFutureException_AndUndoRestoresIt()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var master = new CalendarEvent
            {
                Id = "split-cleanup-master",
                CalendarId = "primary",
                Title = "Daily standup",
                Start = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.FromHours(9)),
                End = new DateTimeOffset(2026, 5, 10, 10, 0, 0, TimeSpan.FromHours(9)),
                RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=5\"]"
            };
            await repository.SaveEventAsync(master);
            await repository.SaveEventAsync(new CalendarEvent
            {
                Id = "future-exception",
                CalendarId = "primary",
                Title = "Future exception",
                Start = new DateTimeOffset(2026, 5, 13, 11, 0, 0, TimeSpan.FromHours(9)),
                End = new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.FromHours(9)),
                RecurringParentId = master.Id,
                OriginalStart = new DateTimeOffset(2026, 5, 13, 9, 0, 0, TimeSpan.FromHours(9)),
                IsRecurrenceException = true
            });

            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            await viewModel.NavigateToDateAsync(new DateTime(2026, 5, 12));
            var occurrence = viewModel.CalendarDays
                .Single(day => day.Date == new DateTime(2026, 5, 12))
                .Segments
                .Single(segment => segment.Event?.Title == "Daily standup")
                .Event!;
            viewModel.SelectEvent(occurrence);
            viewModel.Title = "Changed future";

            await viewModel.SaveCurrentEventAsync(RecurrenceEditScope.ThisAndFollowing);

            var originalAfterSplit = await repository.FindEventByIdAsync("future-exception");
            Assert.True(originalAfterSplit is null || originalAfterSplit.IsDeleted,
                "The old-series exception must not remain as an active hidden row after the split.");

            var splitRows = await LoadMayRowsAsync(repository);
            var futureMaster = Assert.Single(splitRows, item =>
                item.Id != master.Id && item.IsRecurringMaster && !item.IsDeleted);
            Assert.Single(splitRows, item =>
                item.Id != "future-exception"
                && item.IsRecurrenceException
                && !item.IsDeleted
                && item.RecurringParentId == futureMaster.Id);

            Assert.True(await viewModel.UndoLastChangeAsync());

            var restoredOriginal = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync("future-exception"));
            Assert.False(restoredOriginal.IsDeleted);
            Assert.Equal(master.Id, restoredOriginal.RecurringParentId);
            Assert.Equal("Future exception", restoredOriginal.Title);

            var restoredRows = await LoadMayRowsAsync(repository);
            Assert.Single(restoredRows, item => item.IsRecurrenceException && !item.IsDeleted);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
        }
    }

    private static Task<IReadOnlyList<CalendarEvent>> LoadMayRowsAsync(CalendarRepository repository) =>
        repository.LoadEventsAsync(
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(9)),
            includeDeleted: true);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
