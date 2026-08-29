using System.Net;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Google;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class RecurrenceExceptionRecoveryRegressionTests
{
    [Fact]
    public async Task SyncAsync_WhenRecurrenceInstanceUpdateReturnsNotFound_DoesNotInsertStandaloneEvent()
    {
        await using var fixture = await SyncFixture.CreateAsync();

        var result = await fixture.Service.SyncAsync(fixture.Settings);

        Assert.Equal(0, fixture.Client.InsertCount);
        Assert.True(result.Failed > 0);
        Assert.Equal(0, result.Recreated);
        var persisted = await fixture.Repository.FindEventByIdAsync("exception-local");
        Assert.NotNull(persisted);
        Assert.True(persisted!.IsDirty);
        Assert.Equal("instance-remote", persisted.GoogleEventId);
    }

    [Fact]
    public async Task SyncAsync_WhenRecurrenceInstanceCannotBeResolved_DoesNotInsertStandaloneEvent()
    {
        await using var fixture = await SyncFixture.CreateAsync(includeRemoteInstanceId: false);

        var result = await fixture.Service.SyncAsync(fixture.Settings);

        Assert.Equal(0, fixture.Client.InsertCount);
        Assert.True(result.Failed > 0);
        Assert.Equal(0, result.Recreated);
        var persisted = await fixture.Repository.FindEventByIdAsync("exception-local");
        Assert.NotNull(persisted);
        Assert.True(persisted!.IsDirty);
        Assert.Null(persisted.GoogleEventId);
    }

    private sealed class SyncFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly string _oauthPath;

        private SyncFixture(
            string databasePath,
            string oauthPath,
            CalendarRepository repository,
            NotFoundInstanceClient client,
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
        public NotFoundInstanceClient Client { get; }
        public GoogleCalendarSyncService Service { get; }
        public AppSettings Settings { get; }

        public static async Task<SyncFixture> CreateAsync(bool includeRemoteInstanceId = true)
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"recurrence-recovery-{Guid.NewGuid():N}.db");
            var oauthPath = Path.Combine(Path.GetTempPath(), $"oauth-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(oauthPath, "{}");

            var repository = new CalendarRepository(databasePath);
            await repository.InitializeAsync();
            var originalStart = new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero);
            await repository.SaveEventAsync(new CalendarEvent
            {
                Id = "series-local",
                GoogleEventId = "series-remote",
                CalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
                Title = "Series",
                Start = originalStart,
                End = originalStart.AddHours(1),
                RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=3\"]",
                IsDirty = false,
                LastSyncedAt = DateTimeOffset.Now
            });
            await repository.SaveEventAsync(new CalendarEvent
            {
                Id = "exception-local",
                GoogleEventId = includeRemoteInstanceId ? "instance-remote" : null,
                RecurringEventId = "series-remote",
                RecurringParentId = "series-local",
                OriginalStart = originalStart,
                IsRecurrenceException = true,
                CalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
                Title = "Moved instance",
                Start = originalStart.AddHours(3),
                End = originalStart.AddHours(4),
                IsDirty = true,
                LastSyncedAt = originalStart.AddDays(-1)
            });
            await repository.SaveSyncTokenAsync(GoogleCalendarDefaults.PrimaryCalendarId, "sync-token-1");

            var client = new NotFoundInstanceClient();
            var service = new GoogleCalendarSyncService(repository, new RecordingApi(client));
            var settings = new AppSettings
            {
                OAuthClientJsonPath = oauthPath,
                ActiveCalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
                SyncConflictPolicy = SyncConflictPolicy.PreferLocal
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

    private sealed class RecordingApi(NotFoundInstanceClient client) : IGoogleCalendarApi
    {
        public Task<IGoogleCalendarClient> CreateClientAsync(string clientJsonPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IGoogleCalendarClient>(client);

        public Task<IReadOnlyDictionary<string, EventDisplayColors>> LoadEventColorPaletteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, EventDisplayColors>>(new Dictionary<string, EventDisplayColors>());

        public Task ClearTokensAsync() => Task.CompletedTask;
    }

    private sealed class NotFoundInstanceClient : IGoogleCalendarClient
    {
        public int InsertCount { get; private set; }

        public Task<IReadOnlyList<GoogleCalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GoogleCalendarInfo>>([]);

        public Task<GoogleEventPage> ListEventsAsync(GoogleEventListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GoogleEventPage([], null, "next-sync-token"));

        public Task<Event> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default) =>
            throw new GoogleApiException("calendar", "instance not found") { HttpStatusCode = HttpStatusCode.NotFound };

        public Task<Event> InsertEventAsync(string calendarId, Event googleEvent, CancellationToken cancellationToken = default)
        {
            InsertCount++;
            return Task.FromResult(new Event
            {
                Id = "standalone-recreated",
                ETag = "recreated-etag",
                Summary = googleEvent.Summary,
                Start = googleEvent.Start,
                End = googleEvent.End
            });
        }

        public Task<Event> UpdateEventAsync(string calendarId, string eventId, Event googleEvent, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Event>> ListInstancesAsync(
            string calendarId,
            string recurringEventId,
            DateTimeOffset timeMin,
            DateTimeOffset timeMax,
            bool showDeleted,
            int maxResults,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Event>>([]);
    }
}