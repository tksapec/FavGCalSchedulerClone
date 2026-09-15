using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class SyncConcurrentEditClassificationRegressionTests
{
    [Fact]
    public async Task SyncAsync_LocalEditAfterPlanSnapshot_IsSkippedWithoutFalseConflict()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"sync-concurrent-edit-{Guid.NewGuid():N}.db");
        var oauthPath = Path.Combine(Path.GetTempPath(), $"oauth-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(oauthPath, "{}");
        var repository = new CalendarRepository(databasePath);

        try
        {
            await repository.InitializeAsync();
            var original = new CalendarEvent
            {
                Id = "local-edit-race",
                CalendarId = "work",
                GoogleEventId = "remote-edit-race",
                LastSyncedGoogleEtag = "etag-1",
                Title = "Original",
                Start = new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.FromHours(9)),
                End = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.FromHours(9)),
                IsDirty = false
            };
            await repository.UpsertSyncedEventAsync(original);

            var firstEdit = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
            firstEdit.Title = "First edit";
            firstEdit.IsDirty = true;
            await repository.SaveEventAsync(firstEdit);

            var remote = new Event
            {
                Id = original.GoogleEventId,
                ETag = original.LastSyncedGoogleEtag,
                Summary = "Original",
                Start = new EventDateTime { DateTimeDateTimeOffset = original.Start },
                End = new EventDateTime { DateTimeDateTimeOffset = original.End },
                Status = "confirmed"
            };
            var client = new EditDuringListClient(repository, original.Id, remote);
            var service = new GoogleCalendarSyncService(repository, new RecordingApi(client));
            var settings = new AppSettings
            {
                OAuthClientJsonPath = oauthPath,
                ActiveCalendarId = "work",
                VisibleCalendarIds = ["work"],
                SyncConflictPolicy = SyncConflictPolicy.SkipLocalDirty
            };

            var result = await service.SyncAsync(settings);

            Assert.Equal(0, result.Pushed);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, result.Conflicts);
            Assert.Equal(0, result.Failed);
            Assert.Equal(0, client.UpdateCount);
            Assert.Null(await repository.GetSyncTokenAsync("work"));

            var stored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
            Assert.Equal("Second edit", stored.Title);
            Assert.True(stored.IsDirty);
        }
        finally
        {
            await repository.BeginMaintenanceAsync();
            SqliteConnection.ClearAllPools();
            DeleteIfExists(databasePath);
            DeleteIfExists(databasePath + "-wal");
            DeleteIfExists(databasePath + "-shm");
            DeleteIfExists(oauthPath);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class RecordingApi(EditDuringListClient client) : IGoogleCalendarApi
    {
        public Task<IGoogleCalendarClient> CreateClientAsync(string clientJsonPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IGoogleCalendarClient>(client);

        public Task<IReadOnlyDictionary<string, EventDisplayColors>> LoadEventColorPaletteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, EventDisplayColors>>(new Dictionary<string, EventDisplayColors>());

        public Task ClearTokensAsync() => Task.CompletedTask;
    }

    private sealed class EditDuringListClient(CalendarRepository repository, string localId, Event remoteEvent) : IGoogleCalendarClient
    {
        private int _listCount;
        private int _updateCount;

        public int UpdateCount => Volatile.Read(ref _updateCount);

        public Task<IReadOnlyList<GoogleCalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GoogleCalendarInfo>>([]);

        public async Task<GoogleEventPage> ListEventsAsync(GoogleEventListRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _listCount) == 1)
            {
                var newer = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(localId));
                newer.Title = "Second edit";
                newer.IsDirty = true;
                await repository.SaveEventAsync(newer);
            }

            return new GoogleEventPage([remoteEvent], null, "token-after-list");
        }

        public Task<Event> InsertEventAsync(string calendarId, Event googleEvent, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Event> UpdateEventAsync(string calendarId, string eventId, Event googleEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _updateCount);
            return Task.FromResult(googleEvent);
        }

        public Task DeleteEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Event> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(remoteEvent);

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
