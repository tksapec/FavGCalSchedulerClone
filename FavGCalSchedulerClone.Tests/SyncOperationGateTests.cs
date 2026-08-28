using System.Reflection;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class SyncOperationGateTests
{
    [Fact]
    public async Task ClearSyncDiagnosticsAsync_WaitsForExistingSyncDataOperation()
    {
        var viewModel = await CreateViewModelAsync();
        var gate = GetSyncDataOperationGate(viewModel);
        await gate.WaitAsync();
        Task clearTask;
        try
        {
            clearTask = viewModel.ClearSyncDiagnosticsAsync();
            Assert.False(clearTask.IsCompleted);
        }
        finally
        {
            gate.Release();
        }

        await clearTask;
    }

    [Fact]
    public async Task SynchronizeDirtyOnlyAsync_WaitsBeforeSelectingDirtyTargets()
    {
        var viewModel = await CreateViewModelAsync();
        var gate = GetSyncDataOperationGate(viewModel);
        await gate.WaitAsync();
        Task syncTask;
        try
        {
            syncTask = viewModel.SynchronizeDirtyOnlyAsync();
            Assert.False(syncTask.IsCompleted);
        }
        finally
        {
            gate.Release();
        }

        await syncTask;
    }

    [Fact]
    public async Task ResyncFailedItemsAsync_WaitsBeforeFilteringCurrentDirtyTargets()
    {
        var viewModel = await CreateViewModelAsync();
        var gate = GetSyncDataOperationGate(viewModel);
        await gate.WaitAsync();
        Task retryTask;
        try
        {
            retryTask = viewModel.ResyncFailedItemsAsync(["missing-id"]);
            Assert.False(retryTask.IsCompleted);
        }
        finally
        {
            gate.Release();
        }

        await retryTask;
    }

    [Fact]
    public async Task LoadSyncDiagnosticsAsync_WaitsForStableSyncDataSnapshot()
    {
        var viewModel = await CreateViewModelAsync();
        var gate = GetSyncDataOperationGate(viewModel);
        await gate.WaitAsync();
        Task diagnosticsTask;
        try
        {
            diagnosticsTask = viewModel.LoadSyncDiagnosticsAsync();
            Assert.False(diagnosticsTask.IsCompleted);
        }
        finally
        {
            gate.Release();
        }

        await diagnosticsTask;
    }

    [Fact]
    public async Task ReloadAvailableCalendarsAsync_WaitsForExistingGoogleOperation()
    {
        var viewModel = await CreateViewModelAsync();
        var gate = GetSyncDataOperationGate(viewModel);
        await gate.WaitAsync();
        Task reloadTask;
        try
        {
            reloadTask = viewModel.ReloadAvailableCalendarsAsync();
            Assert.False(reloadTask.IsCompleted);
        }
        finally
        {
            gate.Release();
        }

        await reloadTask;
    }

    [Fact]
    public async Task ClearTokensAsync_WaitsForExistingGoogleOperationWithoutTouchingRealTokenStore()
    {
        var googleApi = new RecordingGoogleCalendarApi();
        var viewModel = await CreateViewModelAsync(googleApi);
        var gate = GetSyncDataOperationGate(viewModel);
        await gate.WaitAsync();
        Task clearTokensTask;
        try
        {
            clearTokensTask = viewModel.ClearTokensAsync();
            Assert.False(clearTokensTask.IsCompleted);
            Assert.Equal(0, googleApi.ClearTokensCallCount);
        }
        finally
        {
            gate.Release();
        }

        await clearTokensTask;
        Assert.Equal(1, googleApi.ClearTokensCallCount);
    }

    [Fact]
    public async Task AuthorizeGoogleAsync_WaitsForExistingGoogleOperation()
    {
        var viewModel = await CreateViewModelAsync();
        var gate = GetSyncDataOperationGate(viewModel);
        await gate.WaitAsync();
        Task authorizeTask;
        try
        {
            authorizeTask = viewModel.AuthorizeGoogleAsync();
            Assert.False(authorizeTask.IsCompleted);
        }
        finally
        {
            gate.Release();
        }

        await authorizeTask;
    }

    [Fact]
    public async Task RestoreAllCalendarsAsync_WaitsForExistingSyncDataOperationBeforeReadingBackup()
    {
        var viewModel = await CreateViewModelAsync();
        var gate = GetSyncDataOperationGate(viewModel);
        await gate.WaitAsync();
        Task<RestoreResult> restoreTask;
        try
        {
            restoreTask = viewModel.RestoreAllCalendarsAsync(
                Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.zip"));
            Assert.False(restoreTask.IsCompleted);
        }
        finally
        {
            gate.Release();
        }

        await Assert.ThrowsAnyAsync<Exception>(async () => await restoreTask);
        Assert.Equal(0, GetDatabaseMaintenanceState(viewModel));
    }

    [Fact]
    public async Task RestoreAllCalendarsAsync_ReinitializesWithoutReenteringTheOwnedSyncGate()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"restore-sync-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var targetPath = Path.Combine(directory, "target.db");
        var backupPath = Path.Combine(directory, "backup.zip");
        var backupService = new BackupService();

        var sourceRepository = new CalendarRepository(sourcePath);
        await sourceRepository.InitializeAsync();
        await sourceRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 3 });
        await backupService.CreateBackupAsync(sourcePath, backupPath);

        var targetRepository = new CalendarRepository(targetPath);
        await targetRepository.InitializeAsync();
        var viewModel = new MainViewModel(targetRepository, new GoogleCalendarSyncService(targetRepository, new RecordingGoogleCalendarApi()));
        await viewModel.InitializeAsync();

        await viewModel.RestoreAllCalendarsAsync(backupPath).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, viewModel.StartupTabIndex);
        Assert.Equal(1, GetSyncDataOperationGate(viewModel).CurrentCount);
        Assert.Equal(0, GetDatabaseMaintenanceState(viewModel));
    }

    [Fact]
    public async Task SyncDataOperation_IsRejectedWhileRestoreMaintenanceIsActive()
    {
        var viewModel = await CreateViewModelAsync();
        var maintenanceField = typeof(MainViewModel).GetField(
            "_databaseMaintenanceInProgress",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(maintenanceField);
        maintenanceField!.SetValue(viewModel, 1);
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                viewModel.ClearSyncDiagnosticsAsync());
            Assert.Contains("リストア", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            maintenanceField.SetValue(viewModel, 0);
        }
    }

    private static async Task<MainViewModel> CreateViewModelAsync(IGoogleCalendarApi? googleApi = null)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var syncService = googleApi is null
            ? new GoogleCalendarSyncService(repository)
            : new GoogleCalendarSyncService(repository, googleApi);
        var viewModel = new MainViewModel(repository, syncService);
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static SemaphoreSlim GetSyncDataOperationGate(MainViewModel viewModel)
    {
        var field = typeof(MainViewModel).GetField(
            "_syncDataOperationGate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<SemaphoreSlim>(field!.GetValue(viewModel));
    }

    private static int GetDatabaseMaintenanceState(MainViewModel viewModel)
    {
        var field = typeof(MainViewModel).GetField(
            "_databaseMaintenanceInProgress",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<int>(field!.GetValue(viewModel));
    }

    private sealed class RecordingGoogleCalendarApi : IGoogleCalendarApi
    {
        public int ClearTokensCallCount { get; private set; }

        public Task<IGoogleCalendarClient> CreateClientAsync(string clientJsonPath, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("A Google client should not be created in this gate test.");

        public Task<IReadOnlyDictionary<string, EventDisplayColors>> LoadEventColorPaletteAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, EventDisplayColors>>(
                new Dictionary<string, EventDisplayColors>(StringComparer.Ordinal));

        public Task ClearTokensAsync()
        {
            ClearTokensCallCount++;
            return Task.CompletedTask;
        }
    }
}
