using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReliabilityHardeningBehaviorTests
{
    [Fact]
    public async Task AtomicWriter_RollsBackAllWritesWhenLaterWriteFails()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                await using var trigger = connection.CreateCommand();
                trigger.CommandText = """
                    CREATE TRIGGER fail_second_event
                    BEFORE INSERT ON events
                    WHEN NEW.id = 'fail-second'
                    BEGIN
                        SELECT RAISE(ABORT, 'intentional test failure');
                    END;
                    """;
                await trigger.ExecuteNonQueryAsync();
            }

            var first = CreateEvent("first");
            var second = CreateEvent("fail-second");

            await Assert.ThrowsAsync<SqliteException>(() =>
                CalendarRepositoryAtomicWriter.SaveEventsAsync(repository, [first, second]));

            Assert.Null(await repository.FindEventByIdAsync("first"));
            Assert.Null(await repository.FindEventByIdAsync("fail-second"));
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task UndoLastChangeAsync_WhenDatabaseWriteFails_KeepsUndoAvailable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await repository.SaveEventAsync(CreateEvent("undo-target"));
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            await viewModel.BulkUpdateEventsAsync(
                ["undo-target"],
                new BulkEventUpdateRequest(ColorId: "5"));
            Assert.True(viewModel.CanUndoLastChange);

            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                await using var trigger = connection.CreateCommand();
                trigger.CommandText = """
                    CREATE TRIGGER fail_undo_event
                    BEFORE INSERT ON events
                    WHEN NEW.id = 'undo-target'
                    BEGIN
                        SELECT RAISE(ABORT, 'intentional undo failure');
                    END;
                    """;
                await trigger.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAsync<SqliteException>(() => viewModel.UndoLastChangeAsync());

            Assert.True(viewModel.CanUndoLastChange);
            Assert.Equal("5", (await repository.FindEventByIdAsync("undo-target"))?.ColorId);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task UndoLastChangeAsync_AfterRecurrenceSplit_RemovesFutureSeriesAndRestoresMaster()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var master = CreateDailyMaster("undo-series");
            await repository.SaveEventAsync(master);
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            var occurrence = await SelectOccurrenceAsync(viewModel, new DateTime(2026, 5, 12));
            viewModel.Title = "Changed future";

            await viewModel.SaveCurrentEventAsync(RecurrenceEditScope.ThisAndFollowing);

            var splitEvents = await LoadMayEventsAsync(repository);
            Assert.Equal(2, splitEvents.Count(item => item.IsRecurringMaster && !item.IsDeleted));
            Assert.True(viewModel.CanUndoLastChange);

            Assert.True(await viewModel.UndoLastChangeAsync());

            var restoredEvents = await LoadMayEventsAsync(repository);
            var restoredMaster = Assert.Single(restoredEvents, item => item.IsRecurringMaster && !item.IsDeleted);
            Assert.Equal(master.Id, restoredMaster.Id);
            Assert.Contains("COUNT=5", restoredMaster.RecurrenceJson ?? "");
            Assert.False(viewModel.CanUndoLastChange);
            Assert.Equal(new DateTime(2026, 5, 12), occurrence.Start.Date);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task UndoLastChangeAsync_AfterSyncedSingleOccurrenceEdit_RestoresMasterValuesWithoutDeletingOccurrence()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var master = CreateDailyMaster("synced-occurrence-undo");
            master.GoogleEventId = "remote-master";
            await repository.SaveEventAsync(master);
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            var occurrence = await SelectOccurrenceAsync(viewModel, new DateTime(2026, 5, 12));
            viewModel.Title = "Edited occurrence";
            viewModel.StartTime = "15:00";
            viewModel.EndTime = "16:00";

            await viewModel.SaveCurrentEventAsync(RecurrenceEditScope.ThisOccurrence);

            var edited = Assert.Single(
                await LoadMayEventsAsync(repository),
                item => item.IsRecurrenceException && !item.IsDeleted);
            edited.GoogleEventId = "remote-occurrence";
            edited.LastSyncedGoogleEtag = "etag-occurrence";
            await repository.UpsertSyncedEventAsync(edited);

            Assert.True(await viewModel.UndoLastChangeAsync());

            var restored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(edited.Id));
            Assert.False(restored.IsDeleted);
            Assert.True(restored.IsRecurrenceException);
            Assert.Equal("remote-occurrence", restored.GoogleEventId);
            Assert.Equal(master.Title, restored.Title);
            Assert.Equal(occurrence.OriginalStart, restored.OriginalStart);
            Assert.Equal(occurrence.OriginalStart, restored.Start);
            Assert.Equal(occurrence.OriginalStart!.Value + (master.End - master.Start), restored.End);
            Assert.True(restored.IsDirty);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task SaveCurrentEventAsync_ThisAndFollowingCalendarMove_AlignsFutureExceptionsWithFutureMaster()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var master = CreateDailyMaster("split-calendar");
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
            AddDestinationCalendar(viewModel);
            await SelectOccurrenceAsync(viewModel, new DateTime(2026, 5, 12));
            viewModel.EditorCalendarId = "destination";

            await viewModel.SaveCurrentEventAsync(RecurrenceEditScope.ThisAndFollowing);

            var events = await LoadMayEventsAsync(repository);
            var futureMaster = Assert.Single(
                events,
                item => item.Id != master.Id && item.IsRecurringMaster && !item.IsDeleted);
            var movedException = Assert.Single(
                events,
                item => item.Id != "future-exception"
                    && item.IsRecurrenceException
                    && item.RecurringParentId == futureMaster.Id);
            Assert.Equal("destination", futureMaster.CalendarId);
            Assert.Equal(futureMaster.CalendarId, movedException.CalendarId);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task SaveCurrentEventAsync_AllEventsCalendarMove_MovesExistingExceptionsAndUndoRestoresThem()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var master = CreateDailyMaster("move-series");
            await repository.SaveEventAsync(master);
            var exception = new CalendarEvent
            {
                Id = "move-series-exception",
                CalendarId = "primary",
                Title = "Moved exception",
                Start = new DateTimeOffset(2026, 5, 12, 11, 0, 0, TimeSpan.FromHours(9)),
                End = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.FromHours(9)),
                RecurringParentId = master.Id,
                OriginalStart = new DateTimeOffset(2026, 5, 12, 9, 0, 0, TimeSpan.FromHours(9)),
                IsRecurrenceException = true
            };
            await repository.SaveEventAsync(exception);
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            AddDestinationCalendar(viewModel);
            viewModel.SelectEvent(Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(master.Id)));
            viewModel.EditorCalendarId = "destination";

            await viewModel.SaveCurrentEventAsync(RecurrenceEditScope.AllEvents);

            var movedMaster = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(master.Id));
            var movedException = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(exception.Id));
            Assert.Equal("destination", movedMaster.CalendarId);
            Assert.Equal("destination", movedException.CalendarId);
            Assert.Equal(movedMaster.Id, movedException.RecurringParentId);
            Assert.Null(movedException.GoogleEventId);
            Assert.True(viewModel.CanUndoLastChange);

            Assert.True(await viewModel.UndoLastChangeAsync());

            var restoredMaster = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(master.Id));
            var restoredException = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(exception.Id));
            Assert.Equal("primary", restoredMaster.CalendarId);
            Assert.Equal("primary", restoredException.CalendarId);
            Assert.Equal(master.Id, restoredException.RecurringParentId);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Restore_MovesExistingWalAndShmBesideRollbackDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"FavGCalSchedulerClone-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var targetPath = Path.Combine(directory, "target.db");
        var backupPath = Path.Combine(directory, "backup.zip");
        try
        {
            var source = new CalendarRepository(sourcePath);
            await source.InitializeAsync();
            await source.SaveEventAsync(CreateEvent("source"));
            var target = new CalendarRepository(targetPath);
            await target.InitializeAsync();
            await target.SaveEventAsync(CreateEvent("target"));

            var service = new BackupService();
            await service.CreateBackupAsync(sourcePath, backupPath);
            SqliteConnection.ClearAllPools();
            await File.WriteAllTextAsync(targetPath + "-wal", "stale-wal");
            await File.WriteAllTextAsync(targetPath + "-shm", "stale-shm");

            var result = await service.RestoreBackupAsync(backupPath, targetPath);

            Assert.NotNull(result.PreviousDatabaseBackupPath);
            Assert.Equal("stale-wal", await File.ReadAllTextAsync(result.PreviousDatabaseBackupPath! + "-wal"));
            Assert.Equal("stale-shm", await File.ReadAllTextAsync(result.PreviousDatabaseBackupPath! + "-shm"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+SUM(A1:A2)")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1:A2)")]
    [InlineData("＝1+1")]
    [InlineData("＋SUM(A1:A2)")]
    [InlineData("－2+3")]
    [InlineData("＠SUM(A1:A2)")]
    public void CsvCellSanitizer_NeutralizesAndRestoresFormulaPrefixes(string value)
    {
        var neutralized = CsvCellSanitizer.NeutralizeForSpreadsheet(value);

        Assert.StartsWith("'", neutralized, StringComparison.Ordinal);
        Assert.Equal(value, CsvCellSanitizer.RestoreNeutralizedValue(neutralized));
    }

    private static CalendarEvent CreateEvent(string id) => new()
    {
        Id = id,
        CalendarId = "primary",
        Title = id,
        Start = new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.FromHours(9)),
        End = new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.FromHours(9)),
        IsDirty = true
    };

    private static CalendarEvent CreateDailyMaster(string id) => new()
    {
        Id = id,
        CalendarId = "primary",
        Title = "Daily standup",
        Start = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.FromHours(9)),
        End = new DateTimeOffset(2026, 5, 10, 10, 0, 0, TimeSpan.FromHours(9)),
        RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=5\"]"
    };

    private static async Task<CalendarEvent> SelectOccurrenceAsync(MainViewModel viewModel, DateTime date)
    {
        await viewModel.NavigateToDateAsync(date);
        var occurrence = viewModel.CalendarDays
            .Single(day => day.Date == date.Date)
            .Segments
            .Single(segment => segment.Event?.Title == "Daily standup")
            .Event!;
        viewModel.SelectEvent(occurrence);
        return occurrence;
    }

    private static Task<IReadOnlyList<CalendarEvent>> LoadMayEventsAsync(CalendarRepository repository) =>
        repository.LoadEventsAsync(
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(9)),
            includeDeleted: true);

    private static void AddDestinationCalendar(MainViewModel viewModel)
    {
        viewModel.AvailableCalendars.Add(new GoogleCalendarSelectionItem
        {
            Id = "destination",
            Summary = "Destination"
        });
    }

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
