using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class RestoreRestartRequiredLatchRegressionTests
{
    private static readonly string AppRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App"));

    [Fact]
    public async Task ReloadFailure_LatchesDatabaseOperationsUntilProcessRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"restore-restart-latch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var targetPath = Path.Combine(directory, "calendar.db");
        var backupPath = Path.Combine(directory, "restore.zip");
        var postFailureBackupPath = Path.Combine(directory, "must-not-run.zip");
        var backupService = new BackupService();

        try
        {
            var sourceRepository = new CalendarRepository(sourcePath);
            await sourceRepository.InitializeAsync();
            await sourceRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 7 });
            await backupService.CreateBackupAsync(sourcePath, backupPath);

            var targetRepository = new CalendarRepository(targetPath);
            await targetRepository.InitializeAsync();
            await targetRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 1 });

            var viewModel = new MainViewModel(
                targetRepository,
                new GoogleCalendarSyncService(targetRepository),
                backupService,
                new CalendarCsvService(),
                new FavGCalSchedulerImportService(targetRepository));
            await viewModel.InitializeAsync();
            viewModel.BeforeLoadCalendarSnapshotAsync = (_, _) =>
                throw new InvalidOperationException("forced reload failure");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                viewModel.RestoreAllCalendarsAsync(backupPath));

            Assert.True(viewModel.IsDatabaseRestartRequired);
            Assert.False(viewModel.IsDatabaseMaintenanceInProgress);
            Assert.Equal(7, (await targetRepository.LoadSettingsAsync()).StartupTabIndex);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                viewModel.SynchronizeDirtyOnlyAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                viewModel.BackupAllCalendarsAsync(postFailureBackupPath));
            Assert.False(File.Exists(postFailureBackupPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReloadFailure_DoesNotRestartReminderMonitoring()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            AppRoot, "ViewModels", "MainViewModel.BackupRestore.cs"));
        var reloadCall = source.IndexOf("ReloadRestoredViewModelStateAsync", StringComparison.Ordinal);
        var reloadCatch = source.IndexOf("catch (Exception ex)", reloadCall, StringComparison.Ordinal);
        var message = source.IndexOf("バックアップDBの復元は完了しましたが、画面状態の再読み込みに失敗しました", reloadCatch, StringComparison.Ordinal);
        var stopReminderRestart = source.IndexOf("reminderWasRunning = false;", reloadCatch, StringComparison.Ordinal);

        Assert.True(reloadCall >= 0 && reloadCatch > reloadCall);
        Assert.True(message > reloadCatch && stopReminderRestart > reloadCatch && stopReminderRestart < message,
            "A partial ViewModel reload failure must prevent reminder monitoring from being restarted against uncertain in-memory state.");
    }

    [Fact]
    public async Task RestartRequiredState_SuppressesQueuedSettingsPersistence()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            AppRoot, "ViewModels", "MainViewModel.Settings.cs"));
        var persistStart = source.IndexOf("private async Task PersistSettingsAsync", StringComparison.Ordinal);
        var save = source.IndexOf("await _repository.SaveSettingsAsync(request.Settings)", persistStart, StringComparison.Ordinal);
        var restartCheck = source.IndexOf("if (IsDatabaseRestartRequired)", persistStart, StringComparison.Ordinal);

        Assert.True(persistStart >= 0 && restartCheck > persistStart && save > restartCheck,
            "Queued settings writes must be discarded after a partial restore reload failure.");
    }

    [Fact]
    public async Task Restore_CancelsCalendarBackgroundWorkBeforeDatabaseMaintenanceAndAfterPartialReloadFailure()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            AppRoot, "ViewModels", "MainViewModel.BackupRestore.cs"));
        var restoreStart = source.IndexOf("public async Task<RestoreResult> RestoreAllCalendarsAsync", StringComparison.Ordinal);
        var beginRepositoryMaintenance = source.IndexOf("await _repository.BeginMaintenanceAsync();", restoreStart, StringComparison.Ordinal);
        var firstCancellation = source.IndexOf("CancelCalendarBackgroundWorkForDatabaseMaintenance();", restoreStart, StringComparison.Ordinal);
        var reloadCall = source.IndexOf("ReloadRestoredViewModelStateAsync", beginRepositoryMaintenance, StringComparison.Ordinal);
        var reloadCatch = source.IndexOf("catch (Exception ex)", reloadCall, StringComparison.Ordinal);
        var secondCancellation = source.IndexOf("CancelCalendarBackgroundWorkForDatabaseMaintenance();", reloadCatch, StringComparison.Ordinal);
        var restartLatch = source.IndexOf("MarkDatabaseRestartRequired();", reloadCatch, StringComparison.Ordinal);

        Assert.True(restoreStart >= 0 && firstCancellation > restoreStart && firstCancellation < beginRepositoryMaintenance,
            "Restore must cancel stale calendar refresh/prefetch work before repository maintenance begins.");
        Assert.True(reloadCatch > reloadCall && secondCancellation > reloadCatch && secondCancellation < restartLatch,
            "A partial post-restore reload failure must cancel any refresh/prefetch work started during reconstruction before the restart latch is exposed.");
    }

    [Fact]
    public async Task OperationalStatusRefresh_DoesNotReadReminderDiagnosticsDuringRestoreOrRestartRequiredState()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf("private async Task RefreshOperationalStatusAsync()", StringComparison.Ordinal);
        var diagnosticsRead = source.IndexOf("_reminderService.LoadDiagnosticsAsync()", methodStart, StringComparison.Ordinal);
        var maintenanceCheck = source.IndexOf("_viewModel.IsDatabaseMaintenanceInProgress", methodStart, StringComparison.Ordinal);
        var restartCheck = source.IndexOf("_viewModel.IsDatabaseRestartRequired", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && diagnosticsRead > methodStart);
        Assert.True(maintenanceCheck > methodStart && maintenanceCheck < diagnosticsRead,
            "Operational status refresh must skip reminder DB diagnostics while restore maintenance is active.");
        Assert.True(restartCheck > methodStart && restartCheck < diagnosticsRead,
            "Operational status refresh must remain disabled after a partial restore until restart.");
    }

    [Fact]
    public async Task AppDeactivation_DoesNotNavigateCalendarDuringRestoreOrRestartRequiredState()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "App.xaml.cs"));
        var methodStart = source.IndexOf("private bool ShouldSkipReturnToToday()", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private async Task CompleteStartupInitializationAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && nextMethod > methodStart);
        var method = source[methodStart..nextMethod];

        Assert.Contains("viewModel.IsDatabaseMaintenanceInProgress", method, StringComparison.Ordinal);
        Assert.Contains("viewModel.IsDatabaseRestartRequired", method, StringComparison.Ordinal);
    }
}
