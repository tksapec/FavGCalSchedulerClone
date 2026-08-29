using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class AutomaticSyncRerunRegressionTests
{
    [Fact]
    public async Task RunAutomaticSyncIfDueAsync_WhenSecondTickArrivesDuringActiveSync_DoesNotRunImmediateDuplicate()
    {
        await using var fixture = await SyncFixture.CreateAsync();

        var firstSync = fixture.ViewModel.RunAutomaticSyncIfDueAsync();
        await fixture.Client.FirstListStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await fixture.ViewModel.RunAutomaticSyncIfDueAsync();
        fixture.Client.ReleaseFirstList.TrySetResult(true);
        await firstSync;

        Assert.Equal(1, fixture.Client.ListEventsCount);
        Assert.NotNull(fixture.ViewModel.CreateSettingsSnapshot().LastAutomaticSyncAt);
    }

    private sealed class SyncFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly string _oauthPath;

        private SyncFixture(
            string databasePath,
            string oauthPath,
            BlockingClient client,
            MainViewModel viewModel)
        {
            _databasePath = databasePath;
            _oauthPath = oauthPath;
            Client = client;
            ViewModel = viewModel;
        }

        public BlockingClient Client { get; }
        public MainViewModel ViewModel { get; }

        public static async Task<SyncFixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"automatic-sync-rerun-{Guid.NewGuid():N}.db");
            var oauthPath = Path.Combine(Path.GetTempPath(), $"oauth-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(oauthPath, "{}");

            var repository = new CalendarRepository(databasePath);
            await repository.InitializeAsync();
            var client = new BlockingClient();
            var syncService = new GoogleCalendarSyncService(repository, new RecordingApi(client));
            var viewModel = new MainViewModel(repository, syncService);
            var settings = viewModel.CreateSettingsSnapshot();
            settings.OAuthClientJsonPath = oauthPath;
            settings.AutomaticSyncIntervalMinutes = 30;
            settings.LastAutomaticSyncAt = null;
            await viewModel.SaveApplicationSettingsAsync(settings);
            return new SyncFixture(databasePath, oauthPath, client, viewModel);
        }

        public ValueTask DisposeAsync()
        {
            Client.ReleaseFirstList.TrySetResult(true);
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

    private sealed class RecordingApi(BlockingClient client) : IGoogleCalendarApi
    {
        public Task<IGoogleCalendarClient> CreateClientAsync(string clientJsonPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IGoogleCalendarClient>(client);

        public Task<IReadOnlyDictionary<string, EventDisplayColors>> LoadEventColorPaletteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, EventDisplayColors>>(new Dictionary<string, EventDisplayColors>());

        public Task ClearTokensAsync() => Task.CompletedTask;
    }

    private sealed class BlockingClient : IGoogleCalendarClient
    {
        private int _listEventsCount;

        public int ListEventsCount => Volatile.Read(ref _listEventsCount);
        public TaskCompletionSource<bool> FirstListStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseFirstList { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<GoogleCalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GoogleCalendarInfo>>([]);

        public async Task<GoogleEventPage> ListEventsAsync(GoogleEventListRequest request, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _listEventsCount);
            if (call == 1)
            {
                FirstListStarted.TrySetResult(true);
                await ReleaseFirstList.Task.WaitAsync(cancellationToken);
            }

            return new GoogleEventPage([], null, $"sync-token-{call}");
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
