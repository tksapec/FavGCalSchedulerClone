using System.Reflection;
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

    private static async Task<MainViewModel> CreateViewModelAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
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
}
