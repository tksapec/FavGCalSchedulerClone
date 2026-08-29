using System.Net;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Google;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class InitialSyncRecurrenceWindowRegressionTests
{
    [Fact]
    public async Task SyncAsync_InitialParentEventRequest_DoesNotUseTimeMinCutoff()
    {
        await using var fixture = await SyncFixture.CreateAsync();

        await fixture.Service.SyncAsync(fixture.Settings);

        var request = Assert.Single(fixture.Client.Requests);
        AssertFullParentRequestWithoutTimeMin(request);
    }

    [Fact]
    public async Task SyncAsync_IncrementalRequest_UsesSyncTokenWithoutTimeFilter()
    {
        await using var fixture = await SyncFixture.CreateAsync();
        await fixture.Repository.SaveSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId, "sync-token-1");

        await fixture.Service.SyncAsync(fixture.Settings);

        var request = Assert.Single(fixture.Client.Requests);
        Assert.Equal("sync-token-1", request.SyncToken);
        Assert.Null(request.TimeMin);
        Assert.False(request.SingleEvents);
    }

    [Fact]
    public async Task SyncAsync_WhenSyncTokenExpires_RecoveryFullRequestDoesNotUseTimeMinCutoff()
    {
        await using var fixture = await SyncFixture.CreateAsync(throwGoneOnFirstList: true);
        await fixture.Repository.SaveSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId, "expired-sync-token");

        await fixture.Service.SyncAsync(fixture.Settings);

        Assert.Equal(2, fixture.Client.Requests.Count);
        var incremental = fixture.Client.Requests[0];
        var recovery = fixture.Client.Requests[1];
        Assert.Equal("expired-sync-token", incremental.SyncToken);
        Assert.Null(incremental.TimeMin);
        AssertFullParentRequestWithoutTimeMin(recovery);
    }

    [Fact]
    public async Task SyncAsync_WhenSyncTokenExpires_RemovesCleanGoogleBackedEventMissingFromRecoveryFullSync()
    {
        await using var fixture = await SyncFixture.CreateAsync(throwGoneOnFirstList: true);
        await SeedCleanGoogleBackedEventAsync(fixture.Repository, "stale-sync", "stale-sync-google");
        await fixture.Repository.SaveSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId, "expired-sync-token");

        var result = await fixture.Service.SyncAsync(fixture.Settings);

        Assert.Equal(0, result.Failed);
        Assert.Null(await fixture.Repository.FindEventByIdAsync("stale-sync"));
        Assert.Equal("next-sync-token", await fixture.Repository.GetSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId));
    }

    [Fact]
    public async Task SyncAsync_WhenRecoveryFullSyncFails_PreservesExistingCleanGoogleBackedEvent()
    {
        await using var fixture = await SyncFixture.CreateAsync(throwGoneOnFirstList: true, throwOnSecondList: true);
        await SeedCleanGoogleBackedEventAsync(fixture.Repository, "preserved-sync", "preserved-sync-google");
        await fixture.Repository.SaveSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId, "expired-sync-token");

        var result = await fixture.Service.SyncAsync(fixture.Settings);

        Assert.True(result.Failed > 0);
        Assert.NotNull(await fixture.Repository.FindEventByIdAsync("preserved-sync"));
        Assert.Null(await fixture.Repository.GetSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId));
    }

    [Fact]
    public async Task PullAsync_InitialParentEventRequest_DoesNotUseTimeMinCutoff()
    {
        await using var fixture = await SyncFixture.CreateAsync();

        await fixture.Service.PullAsync(fixture.Settings);

        var request = Assert.Single(fixture.Client.Requests);
        AssertFullParentRequestWithoutTimeMin(request);
    }

    [Fact]
    public async Task PullAsync_InitialFullSync_DoesNotDeleteExistingCleanGoogleBackedEventWhenNoPriorToken()
    {
        await using var fixture = await SyncFixture.CreateAsync();
        await SeedCleanGoogleBackedEventAsync(fixture.Repository, "initial-existing", "initial-existing-google");

        await fixture.Service.PullAsync(fixture.Settings);

        Assert.NotNull(await fixture.Repository.FindEventByIdAsync("initial-existing"));
        Assert.Equal("next-sync-token", await fixture.Repository.GetSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId));
    }

    [Fact]
    public async Task PullAsync_WhenSyncTokenExpires_RecoveryFullRequestDoesNotUseTimeMinCutoff()
    {
        await using var fixture = await SyncFixture.CreateAsync(throwGoneOnFirstList: true);
        await fixture.Repository.SaveSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId, "expired-pull-token");

        await fixture.Service.PullAsync(fixture.Settings);

        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Equal("expired-pull-token", fixture.Client.Requests[0].SyncToken);
        Assert.Null(fixture.Client.Requests[0].TimeMin);
        AssertFullParentRequestWithoutTimeMin(fixture.Client.Requests[1]);
    }

    [Fact]
    public async Task PullAsync_WhenSyncTokenExpires_RemovesCleanGoogleBackedEventMissingFromRecoveryFullSync()
    {
        await using var fixture = await SyncFixture.CreateAsync(throwGoneOnFirstList: true);
        await SeedCleanGoogleBackedEventAsync(fixture.Repository, "stale-pull", "stale-pull-google");
        await fixture.Repository.SaveSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId, "expired-pull-token");

        await fixture.Service.PullAsync(fixture.Settings);

        Assert.Null(await fixture.Repository.FindEventByIdAsync("stale-pull"));
        Assert.Equal("next-sync-token", await fixture.Repository.GetSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId));
    }

    [Fact]
    public async Task PullAsync_WhenSyncTokenExpires_PreservesCleanGoogleBackedEventReturnedByRecoveryFullSync()
    {
        var remoteEvent = CreateGoogleEvent("existing-google", "Remote title");
        await using var fixture = await SyncFixture.CreateAsync(
            throwGoneOnFirstList: true,
            listItems: [remoteEvent]);
        await SeedCleanGoogleBackedEventAsync(fixture.Repository, "existing-local", "existing-google");
        await fixture.Repository.SaveSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId, "expired-pull-token");

        await fixture.Service.PullAsync(fixture.Settings);

        var stored = await fixture.Repository.FindEventByIdAsync("existing-local");
        Assert.NotNull(stored);
        Assert.Equal("existing-google", stored!.GoogleEventId);
        Assert.Equal("Remote title", stored.Title);
        Assert.Equal("next-sync-token", await fixture.Repository.GetSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId));
    }

    [Fact]
    public async Task PullAsync_WhenSyncTokenExpires_PreservesDirtyAndLocalOnlyEventsMissingFromRecoveryFullSync()
    {
        await using var fixture = await SyncFixture.CreateAsync(throwGoneOnFirstList: true);
        await fixture.Repository.SaveEventAsync(CreateEvent("dirty-pull", "dirty-pull-google", isDirty: true));
        await fixture.Repository.SaveEventAsync(CreateEvent("local-only", null, isDirty: false));
        await fixture.Repository.SaveSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId, "expired-pull-token");

        await fixture.Service.PullAsync(fixture.Settings);

        var dirty = await fixture.Repository.FindEventByIdAsync("dirty-pull");
        Assert.NotNull(dirty);
        Assert.True(dirty!.IsDirty);
        Assert.Equal("dirty-pull-google", dirty.GoogleEventId);
        Assert.NotNull(await fixture.Repository.FindEventByIdAsync("local-only"));
        Assert.Equal("next-sync-token", await fixture.Repository.GetSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId));
    }

    private static async Task SeedCleanGoogleBackedEventAsync(CalendarRepository repository, string id, string googleEventId)
    {
        await repository.UpsertSyncedEventAsync(CreateEvent(id, googleEventId, isDirty: false));
    }

    private static CalendarEvent CreateEvent(string id, string? googleEventId, bool isDirty)
    {
        return new CalendarEvent
        {
            Id = id,
            GoogleEventId = googleEventId,
            CalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
            Title = id,
            Start = new DateTimeOffset(2026, 8, 29, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero),
            IsDirty = isDirty
        };
    }

    private static Event CreateGoogleEvent(string id, string summary)
    {
        return new Event
        {
            Id = id,
            Summary = summary,
            ETag = $"etag-{id}",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 8, 29, 9, 0, 0, TimeSpan.Zero)
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero)
            }
        };
    }

    private static void AssertFullParentRequestWithoutTimeMin(GoogleEventListRequest request)
    {
        Assert.Null(request.SyncToken);
        Assert.Null(request.TimeMin);
        Assert.False(request.SingleEvents);
        Assert.True(request.ShowDeleted);
    }

    private sealed class SyncFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly string _oauthPath;

        private SyncFixture(
            string databasePath,
            string oauthPath,
            CalendarRepository repository,
            RecordingClient client,
            GoogleCalendarSyncService service,
            AppSettings settings)
        {
            _databasePath = databasePath;
            _oauthPath = oauthPath;
            Repository = repository;
            Client = client;
            Service = service;
            Settings = settings;
        }

        public CalendarRepository Repository { get; }
        public RecordingClient Client { get; }
        public GoogleCalendarSyncService Service { get; }
        public AppSettings Settings { get; }

        public static async Task<SyncFixture> CreateAsync(
            bool throwGoneOnFirstList = false,
            bool throwOnSecondList = false,
            IReadOnlyList<Event>? listItems = null)
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"initial-sync-window-{Guid.NewGuid():N}.db");
            var oauthPath = Path.Combine(Path.GetTempPath(), $"oauth-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(oauthPath, "{}");

            var repository = new CalendarRepository(databasePath);
            await repository.InitializeAsync();
            var client = new RecordingClient(throwGoneOnFirstList, throwOnSecondList, listItems);
            var service = new GoogleCalendarSyncService(repository, new RecordingApi(client));
            var settings = new AppSettings
            {
                OAuthClientJsonPath = oauthPath,
                ActiveCalendarId = GoogleCalendarDefaults.PrimaryCalendarId
            };
            return new SyncFixture(databasePath, oauthPath, repository, client, service, settings);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(_databasePath);
            DeleteIfExists(_databasePath + "-wal");
            DeleteIfExists(_databasePath + "-shm");
            DeleteIfExists(_oauthPath);
            return ValueTask.CompletedTask;
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class RecordingApi(RecordingClient client) : IGoogleCalendarApi
    {
        public Task<IGoogleCalendarClient> CreateClientAsync(string clientJsonPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IGoogleCalendarClient>(client);

        public Task<IReadOnlyDictionary<string, EventDisplayColors>> LoadEventColorPaletteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, EventDisplayColors>>(new Dictionary<string, EventDisplayColors>());

        public Task ClearTokensAsync() => Task.CompletedTask;
    }

    private sealed class RecordingClient(
        bool throwGoneOnFirstList,
        bool throwOnSecondList,
        IReadOnlyList<Event>? listItems) : IGoogleCalendarClient
    {
        private bool _throwGoneOnFirstList = throwGoneOnFirstList;
        public List<GoogleEventListRequest> Requests { get; } = [];

        public Task<IReadOnlyList<GoogleCalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GoogleCalendarInfo>>([]);

        public Task<GoogleEventPage> ListEventsAsync(GoogleEventListRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (_throwGoneOnFirstList)
            {
                _throwGoneOnFirstList = false;
                throw new GoogleApiException("calendar", "sync token expired") { HttpStatusCode = HttpStatusCode.Gone };
            }

            if (throwOnSecondList && Requests.Count == 2)
            {
                throw new HttpRequestException("recovery full sync failed");
            }

            return Task.FromResult(new GoogleEventPage(listItems ?? [], null, "next-sync-token"));
        }

        public Task<Event> InsertEventAsync(string calendarId, Event googleEvent, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Event> UpdateEventAsync(string calendarId, string eventId, Event googleEvent, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Event> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Event>> ListInstancesAsync(
            string calendarId,
            string recurringEventId,
            DateTimeOffset timeMin,
            DateTimeOffset timeMax,
            bool showDeleted,
            int maxResults,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
