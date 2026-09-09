using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Google.Apis.Calendar.v3.Data;

namespace FavGCalSchedulerClone.Tests;

public sealed class SyncPullEditRaceRegressionTests
{
    [Fact]
    public async Task SyncAsync_PreservesLocalEditMadeAfterPullPlanSnapshot()
    {
        var repository = await CreateRepositoryAsync();
        var local = new CalendarEvent
        {
            Id = "pull-race",
            CalendarId = "primary",
            GoogleEventId = "remote-pull-race",
            LastSyncedGoogleEtag = "etag-1",
            Title = "Original",
            Start = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.FromHours(9)),
            IsDirty = false
        };
        await repository.UpsertSyncedEventAsync(local);

        var listStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueList = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new PausingGoogleCalendarApi(
            new Event
            {
                Id = local.GoogleEventId,
                ETag = "etag-2",
                Summary = "Remote changed",
                Status = "confirmed",
                Start = new EventDateTime { DateTimeDateTimeOffset = local.Start },
                End = new EventDateTime { DateTimeDateTimeOffset = local.End }
            },
            listStarted,
            continueList);
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
            await listStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var edited = (await repository.FindEventByIdAsync(local.Id))!;
            edited.Title = "Local edit during pull";
            edited.IsDirty = true;
            await repository.SaveEventAsync(edited);

            continueList.TrySetResult();
            var result = await syncTask;

            var stored = (await repository.FindEventByIdAsync(local.Id))!;
            Assert.Equal("Local edit during pull", stored.Title);
            Assert.True(stored.IsDirty);
            Assert.Equal("etag-1", stored.LastSyncedGoogleEtag);
            Assert.Equal(0, result.Pulled);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(1, result.Conflicts);
            Assert.Null(await repository.GetSyncTokenAsync("primary"));
        }
        finally
        {
            continueList.TrySetResult();
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
        private readonly TaskCompletionSource _listStarted;
        private readonly TaskCompletionSource _continueList;

        public PausingGoogleCalendarApi(
            Event remoteEvent,
            TaskCompletionSource listStarted,
            TaskCompletionSource continueList)
        {
            _remoteEvent = remoteEvent;
            _listStarted = listStarted;
            _continueList = continueList;
        }

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
            => throw new NotSupportedException();

        public Task DeleteEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Event> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default)
            => Task.FromResult(_remoteEvent);

        public async Task<GoogleEventPage> ListEventsAsync(GoogleEventListRequest request, CancellationToken cancellationToken = default)
        {
            _listStarted.TrySetResult();
            await _continueList.Task.WaitAsync(cancellationToken);
            return new GoogleEventPage([_remoteEvent], null, "next-token");
        }

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
