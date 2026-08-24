using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Google.Apis.Calendar.v3.Data;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarMoveUndoRegressionTests
{
    [Fact]
    public async Task CalendarMove_Sync_Undo_Sync_DoesNotLeaveDestinationRemoteEvent()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var settings = CreateSettings();
        await repository.SaveSettingsAsync(settings);
        var api = new CalendarMoveApi();
        var original = new CalendarEvent
        {
            Id = "move-undo",
            CalendarId = "A",
            GoogleEventId = "remote-a",
            LastSyncedGoogleEtag = "etag-a",
            Title = "Move me",
            Start = new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.FromHours(9)),
            IsDirty = false,
            LastSyncedAt = DateTimeOffset.Now.AddMinutes(-5)
        };
        await repository.UpsertSyncedEventAsync(original);
        api.Upsert("A", ToGoogle(original, "remote-a", "etag-a"));
        var sync = new GoogleCalendarSyncService(repository, api);
        var viewModel = new MainViewModel(repository, sync);
        await viewModel.InitializeAsync();

        await viewModel.BulkUpdateEventsAsync([original.Id], new BulkEventUpdateRequest(CalendarId: "B"));
        await sync.SyncAsync(settings);
        var moved = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
        Assert.Equal("B", moved.CalendarId);
        Assert.False(string.IsNullOrWhiteSpace(moved.GoogleEventId));
        Assert.Single(api.Events["B"]);

        Assert.True(await viewModel.UndoLastChangeAsync());
        await sync.SyncAsync(settings);

        Assert.Empty(api.Events["B"]);
        Assert.Single(api.Events["A"]);
        var restored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
        Assert.Equal("A", restored.CalendarId);
        Assert.False(restored.IsDeleted);
    }

    private static AppSettings CreateSettings()
    {
        var jsonPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(jsonPath, "{}");
        return new AppSettings
        {
            OAuthClientJsonPath = jsonPath,
            ActiveCalendarId = "A",
            VisibleCalendarIds = ["A", "B"],
            SyncConflictPolicy = SyncConflictPolicy.PreferLocal,
            SyncAfterLocalChange = false
        };
    }

    private static Event ToGoogle(CalendarEvent local, string id, string etag) => new()
    {
        Id = id,
        ETag = etag,
        Summary = local.Title,
        Start = new EventDateTime { DateTimeDateTimeOffset = local.Start, TimeZone = "Asia/Tokyo" },
        End = new EventDateTime { DateTimeDateTimeOffset = local.End, TimeZone = "Asia/Tokyo" },
        Status = "confirmed",
        Reminders = new Event.RemindersData { UseDefault = false, Overrides = [] }
    };

    private sealed class CalendarMoveApi : IGoogleCalendarApi, IGoogleCalendarClient
    {
        private int _nextId;
        private int _nextEtag;

        public Dictionary<string, Dictionary<string, Event>> Events { get; } = new(StringComparer.Ordinal)
        {
            ["A"] = new(StringComparer.Ordinal),
            ["B"] = new(StringComparer.Ordinal)
        };

        public void Upsert(string calendarId, Event value) => Events[calendarId][value.Id] = value;

        public Task<IGoogleCalendarClient> CreateClientAsync(string clientJsonPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IGoogleCalendarClient>(this);

        public Task<IReadOnlyDictionary<string, EventDisplayColors>> LoadEventColorPaletteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, EventDisplayColors>>(new Dictionary<string, EventDisplayColors>());

        public Task ClearTokensAsync() => Task.CompletedTask;

        public Task<IReadOnlyList<GoogleCalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GoogleCalendarInfo>>([
                new GoogleCalendarInfo("A", "A", []),
                new GoogleCalendarInfo("B", "B", [])
            ]);

        public Task<Event> InsertEventAsync(string calendarId, Event googleEvent, CancellationToken cancellationToken = default)
        {
            googleEvent.Id = $"created-{++_nextId}";
            googleEvent.ETag = $"etag-{++_nextEtag}";
            Events[calendarId][googleEvent.Id] = googleEvent;
            return Task.FromResult(googleEvent);
        }

        public Task<Event> UpdateEventAsync(string calendarId, string eventId, Event googleEvent, CancellationToken cancellationToken = default)
        {
            googleEvent.Id = eventId;
            googleEvent.ETag = $"etag-{++_nextEtag}";
            Events[calendarId][eventId] = googleEvent;
            return Task.FromResult(googleEvent);
        }

        public Task DeleteEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default)
        {
            Events[calendarId].Remove(eventId);
            return Task.CompletedTask;
        }

        public Task<Event> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default)
        {
            if (!Events[calendarId].TryGetValue(eventId, out var value))
            {
                throw new KeyNotFoundException(eventId);
            }
            return Task.FromResult(value);
        }

        public Task<GoogleEventPage> ListEventsAsync(GoogleEventListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GoogleEventPage(Events[request.CalendarId].Values.ToArray(), null, $"token-{request.CalendarId}"));

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
