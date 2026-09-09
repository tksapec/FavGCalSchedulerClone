using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Google.Apis.Calendar.v3.Data;

namespace FavGCalSchedulerClone.Tests;

public sealed class SyncTodoCleanupEditRaceRegressionTests
{
    [Fact]
    public async Task SyncAsync_TodoReminderCleanupDoesNotOverwriteNewerLocalEdit()
    {
        var repository = await CreateRepositoryAsync();
        var local = new CalendarEvent
        {
            Id = "todo-cleanup-race",
            CalendarId = "primary",
            GoogleEventId = "remote-todo-cleanup-race",
            LastSyncedGoogleEtag = "etag-1",
            Title = "#todoA0% Old todo title",
            Description = "Old todo description",
            Start = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.FromHours(9)),
            IsAllDay = true,
            IsDirty = true,
            ReminderMinutesBeforeStart = 30,
            IsAppReminderEnabled = true,
            AppReminderMinutesBeforeStart = [30]
        };
        await repository.SaveEventAsync(local);
        var savedTodo = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.True(savedTodo.IsTodoLike);
        await repository.SaveSyncTokenAsync("primary", "old-token");

        var getStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueGet = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new PausingGoogleCalendarApi(
            new Event
            {
                Id = local.GoogleEventId,
                ETag = "etag-1",
                Summary = local.Title,
                Description = local.Description,
                Status = "confirmed",
                Start = new EventDateTime { Date = "2026-09-10" },
                End = new EventDateTime { Date = "2026-09-11" },
                Reminders = new Event.RemindersData
                {
                    UseDefault = false,
                    Overrides = [new EventReminder { Method = "popup", Minutes = 30 }]
                }
            },
            getStarted,
            continueGet);
        var oauthPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(oauthPath, "{}");
        var settings = new AppSettings
        {
            OAuthClientJsonPath = oauthPath,
            ActiveCalendarId = "primary",
            VisibleCalendarIds = ["primary"],
            SyncConflictPolicy = SyncConflictPolicy.PreferLocal
        };

        try
        {
            var service = new GoogleCalendarSyncService(repository, api);
            var syncTask = service.SyncAsync(settings);
            await getStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var edited = (await repository.FindEventByIdAsync(local.Id))!;
            edited.Title = "#todoA50% Newer local todo title";
            edited.Description = "Newer local todo description";
            edited.IsDirty = true;
            await repository.SaveEventAsync(edited);

            continueGet.TrySetResult();
            var result = await syncTask;

            var stored = (await repository.FindEventByIdAsync(local.Id))!;
            Assert.Equal("#todoA50% Newer local todo title", stored.Title);
            Assert.Equal("Newer local todo description", stored.Description);
            Assert.True(stored.IsDirty);
            Assert.Equal("etag-2", stored.LastSyncedGoogleEtag);
            Assert.Empty(stored.EffectiveAppReminderMinutesBeforeStart);
            Assert.Equal(1, result.Pushed);
        }
        finally
        {
            continueGet.TrySetResult();
            File.Delete(oauthPath);
        }
    }

    [Fact]
    public async Task ApplyTodoReminderCleanupStateAsync_DoesNotClearRemindersAfterTodoBecomesNormalEvent()
    {
        var repository = await CreateRepositoryAsync();
        var local = new CalendarEvent
        {
            Id = "todo-converted-to-normal-race",
            CalendarId = "primary",
            GoogleEventId = "remote-todo-converted-to-normal-race",
            LastSyncedGoogleEtag = "etag-1",
            Title = "#todoA0% Planned todo",
            Description = "Planned todo description",
            Start = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.FromHours(9)),
            IsDirty = true,
            ReminderMinutesBeforeStart = 30,
            IsAppReminderEnabled = true,
            AppReminderMinutesBeforeStart = [30]
        };
        await repository.SaveEventAsync(local);
        var plannedTodo = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.True(plannedTodo.IsTodoLike);

        var converted = (await repository.FindEventByIdAsync(local.Id))!;
        converted.Title = "Normal event after sync planning";
        converted.Description = "No todo marker remains";
        converted.ReminderMinutesBeforeStart = 45;
        converted.IsAppReminderEnabled = true;
        converted.AppReminderMinutesBeforeStart = [45];
        converted.IsDirty = true;
        await repository.SaveEventAsync(converted);

        var current = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.False(current.IsTodoLike);
        Assert.Equal([45], current.EffectiveAppReminderMinutesBeforeStart);

        await repository.ApplyTodoReminderCleanupStateAsync(
            plannedTodo.Id,
            preserveDirtyState: true);

        var stored = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.False(stored.IsTodoLike);
        Assert.True(stored.IsDirty);
        Assert.Equal([45], stored.EffectiveAppReminderMinutesBeforeStart);
        Assert.Equal(45, stored.ReminderMinutesBeforeStart);
    }

    [Fact]
    public async Task SyncAsync_DoesNotRemoveGoogleRemindersAfterPlannedTodoBecomesNormalEvent()
    {
        var repository = await CreateRepositoryAsync();
        var local = new CalendarEvent
        {
            Id = "todo-remote-cleanup-race",
            CalendarId = "primary",
            GoogleEventId = "remote-todo-remote-cleanup-race",
            LastSyncedGoogleEtag = "etag-1",
            Title = "#todoA0% Planned todo",
            Description = "Planned todo description",
            Start = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.FromHours(9)),
            IsAllDay = true,
            IsDirty = false,
            ReminderMinutesBeforeStart = 30,
            IsAppReminderEnabled = true,
            AppReminderMinutesBeforeStart = [30]
        };
        await repository.SaveEventAsync(local);
        var savedTodo = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.True(savedTodo.IsTodoLike);
        Assert.False(savedTodo.IsDirty);
        await repository.SaveSyncTokenAsync("primary", "old-token");

        var getStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueGet = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new PausingGoogleCalendarApi(
            new Event
            {
                Id = local.GoogleEventId,
                ETag = "etag-1",
                Summary = local.Title,
                Description = local.Description,
                Status = "confirmed",
                Start = new EventDateTime { Date = "2026-09-13" },
                End = new EventDateTime { Date = "2026-09-14" },
                Reminders = new Event.RemindersData
                {
                    UseDefault = false,
                    Overrides = [new EventReminder { Method = "popup", Minutes = 30 }]
                }
            },
            getStarted,
            continueGet);
        var oauthPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(oauthPath, "{}");
        var settings = new AppSettings
        {
            OAuthClientJsonPath = oauthPath,
            ActiveCalendarId = "primary",
            VisibleCalendarIds = ["primary"],
            SyncConflictPolicy = SyncConflictPolicy.SkipLocalDirty
        };

        try
        {
            var service = new GoogleCalendarSyncService(repository, api);
            var syncTask = service.SyncAsync(settings);
            await getStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var converted = (await repository.FindEventByIdAsync(local.Id))!;
            converted.Title = "Normal event while sync is planning";
            converted.Description = "No todo marker remains";
            converted.ReminderMinutesBeforeStart = 45;
            converted.IsAppReminderEnabled = true;
            converted.AppReminderMinutesBeforeStart = [45];
            converted.IsDirty = true;
            await repository.SaveEventAsync(converted);

            continueGet.TrySetResult();
            var result = await syncTask;

            var stored = (await repository.FindEventByIdAsync(local.Id))!;
            Assert.False(stored.IsTodoLike);
            Assert.True(stored.IsDirty);
            Assert.Equal([45], stored.EffectiveAppReminderMinutesBeforeStart);
            Assert.Equal(0, api.UpdateCallCount);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(1, result.Conflicts);
        }
        finally
        {
            continueGet.TrySetResult();
            File.Delete(oauthPath);
        }
    }

    private static async Task<CalendarRepository> CreateRepositoryAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        return repository;
    }

    private sealed class PausingGoogleCalendarApi : IGoogleCalendarApi, IGoogleCalendarClient
    {
        private readonly Event _remoteEvent;
        private readonly TaskCompletionSource _getStarted;
        private readonly TaskCompletionSource _continueGet;

        public PausingGoogleCalendarApi(
            Event remoteEvent,
            TaskCompletionSource getStarted,
            TaskCompletionSource continueGet)
        {
            _remoteEvent = remoteEvent;
            _getStarted = getStarted;
            _continueGet = continueGet;
        }

        public int UpdateCallCount { get; private set; }

        public Task<IGoogleCalendarClient> CreateClientAsync(string clientJsonPath, CancellationToken cancellationToken = default)
            => Task.FromResult<IGoogleCalendarClient>(this);

        public Task<IReadOnlyDictionary<string, EventDisplayColors>> LoadEventColorPaletteAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, EventDisplayColors>>(new Dictionary<string, EventDisplayColors>());

        public Task ClearTokensAsync() => Task.CompletedTask;

        public Task<IReadOnlyList<GoogleCalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GoogleCalendarInfo>>([new GoogleCalendarInfo("primary", "Primary")]);

        public Task<Event> InsertEventAsync(string calendarId, Event googleEvent, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Event> UpdateEventAsync(string calendarId, string eventId, Event googleEvent, CancellationToken cancellationToken = default)
        {
            UpdateCallCount++;
            googleEvent.ETag = "etag-2";
            return Task.FromResult(googleEvent);
        }

        public Task DeleteEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async Task<Event> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default)
        {
            _getStarted.TrySetResult();
            await _continueGet.Task.WaitAsync(cancellationToken);
            return _remoteEvent;
        }

        public Task<GoogleEventPage> ListEventsAsync(GoogleEventListRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new GoogleEventPage([], null, "next-token"));

        public Task<IReadOnlyList<Event>> ListInstancesAsync(
            string calendarId,
            string recurringEventId,
            DateTimeOffset timeMin,
            DateTimeOffset timeMax,
            bool showDeleted,
            int maxResults,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Event>>([]);
    }
}
