using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Google.Apis.Calendar.v3.Data;

namespace FavGCalSchedulerClone.Tests;

public sealed class GoogleCalendarIdentityRegressionTests
{
    [Fact]
    public async Task SynchronizeDirtyOnlyAsync_PreviouslySyncedEventWithMissingGoogleId_DoesNotInsertDuplicate()
    {
        var repository = await CreateRepositoryAsync();
        var api = new RecordingGoogleCalendarApi();
        var settings = CreateSettings("work");
        await repository.SaveSettingsAsync(settings);
        var local = new CalendarEvent
        {
            Id = "broken-link",
            CalendarId = "work",
            GoogleEventId = null,
            LastSyncedAt = DateTimeOffset.Now.AddDays(-1),
            Title = "Edited existing event",
            Start = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.FromHours(9)),
            IsDirty = true,
            DirtyFields = "Title"
        };
        await repository.SaveEventAsync(local);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository, api));
        await viewModel.InitializeAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.SynchronizeDirtyOnlyAsync());

        Assert.Contains("Google Event ID", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, api.InsertCount);
        var stored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(local.Id));
        Assert.True(stored.IsDirty);
        Assert.Null(stored.GoogleEventId);
    }

    [Fact]
    public async Task SynchronizeDirtyOnlyAsync_GenuinelyNewLocalEvent_StillInsertsOnce()
    {
        var repository = await CreateRepositoryAsync();
        var api = new RecordingGoogleCalendarApi();
        var settings = CreateSettings("work");
        await repository.SaveSettingsAsync(settings);
        var local = new CalendarEvent
        {
            Id = "new-local",
            CalendarId = "work",
            Title = "New local event",
            Start = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.FromHours(9)),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository, api));
        await viewModel.InitializeAsync();

        var result = await viewModel.SynchronizeDirtyOnlyAsync();

        Assert.Equal(1, api.InsertCount);
        Assert.Equal(0, result.Failed);
        var stored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(local.Id));
        Assert.False(stored.IsDirty);
        Assert.False(string.IsNullOrWhiteSpace(stored.GoogleEventId));
    }

    private static async Task<CalendarRepository> CreateRepositoryAsync()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        return repository;
    }

    private static AppSettings CreateSettings(params string[] calendarIds)
    {
        var jsonPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(jsonPath, "{}");
        return new AppSettings
        {
            OAuthClientJsonPath = jsonPath,
            VisibleCalendarIds = calendarIds.ToList(),
            ActiveCalendarId = calendarIds.FirstOrDefault() ?? GoogleCalendarDefaults.PrimaryCalendarId,
            SyncConflictPolicy = SyncConflictPolicy.PreferLocal
        };
    }

    private sealed class RecordingGoogleCalendarApi : IGoogleCalendarApi, IGoogleCalendarClient
    {
        private int _nextId;

        public int InsertCount { get; private set; }

        public Task<IGoogleCalendarClient> CreateClientAsync(string clientJsonPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IGoogleCalendarClient>(this);

        public Task<IReadOnlyDictionary<string, EventDisplayColors>> LoadEventColorPaletteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, EventDisplayColors>>(new Dictionary<string, EventDisplayColors>());

        public Task ClearTokensAsync() => Task.CompletedTask;

        public Task<IReadOnlyList<GoogleCalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GoogleCalendarInfo>>([new GoogleCalendarInfo("work", "Work", [])]);

        public Task<Event> InsertEventAsync(string calendarId, Event googleEvent, CancellationToken cancellationToken = default)
        {
            InsertCount++;
            googleEvent.Id = $"inserted-{++_nextId}";
            googleEvent.ETag = $"etag-{_nextId}";
            return Task.FromResult(googleEvent);
        }

        public Task<Event> UpdateEventAsync(string calendarId, string eventId, Event googleEvent, CancellationToken cancellationToken = default) =>
            Task.FromResult(googleEvent);

        public Task DeleteEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Event> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default) =>
            throw new KeyNotFoundException(eventId);

        public Task<GoogleEventPage> ListEventsAsync(GoogleEventListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GoogleEventPage([], null, "next-token"));

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
