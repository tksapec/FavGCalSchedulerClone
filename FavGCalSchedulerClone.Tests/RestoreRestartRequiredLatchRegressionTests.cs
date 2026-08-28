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
            await Assert.ThrowsAsync<InvalidOperationException>(() => targetRepository.LoadSettingsAsync());
            var restoredSettings = await targetRepository.RunWithMaintenanceAccessAsync(targetRepository.LoadSettingsAsync);
            Assert.Equal(7, restoredSettings.StartupTabIndex);

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
    public async Task OperationalStatusTimer_IsStoppedForRestoreAndOnlyRestartsAfterNormalMaintenanceCompletion()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.RestoreMaintenance.cs"));
        var applyStart = source.IndexOf("private void ApplyDatabaseMaintenanceInteractionState()", StringComparison.Ordinal);
        var blockedBranch = source.IndexOf("if (_viewModel.IsDatabaseMaintenanceInProgress || _viewModel.IsDatabaseRestartRequired)", applyStart, StringComparison.Ordinal);
        var stopTimer = source.IndexOf("_operationalStatusTimer.Stop();", blockedBranch, StringComparison.Ordinal);
        var blockedReturn = source.IndexOf("return;", blockedBranch, StringComparison.Ordinal);
        var restartTimer = source.IndexOf("_operationalStatusTimer.Start();", blockedReturn, StringComparison.Ordinal);

        Assert.True(applyStart >= 0 && blockedBranch > applyStart);
        Assert.True(stopTimer > blockedBranch && stopTimer < blockedReturn,
            "Operational status polling must stop before restore/restart-required interaction returns.");
        Assert.True(restartTimer > blockedReturn,
            "Operational status polling may restart only after the blocked state has cleared normally.");
    }

    [Fact]
    public async Task Startup_DisablesMainWindowUntilInitializationFinishes()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "Services", "ApplicationStartupService.cs"));
        var initializeStart = source.IndexOf("public async Task InitializeAsync", StringComparison.Ordinal);
        var disableOwner = source.IndexOf("owner.IsEnabled = false;", initializeStart, StringComparison.Ordinal);
        var initializeViewModel = source.IndexOf("await _viewModel.InitializeAsync();", initializeStart, StringComparison.Ordinal);
        var restoreOwner = source.IndexOf("owner.IsEnabled = ownerWasEnabled;", initializeViewModel, StringComparison.Ordinal);

        Assert.True(initializeStart >= 0 && disableOwner > initializeStart && disableOwner < initializeViewModel,
            "The user must not be able to start restore while startup initialization is still using the database.");
        Assert.True(restoreOwner > initializeViewModel,
            "The original interaction state must be restored after startup initialization finishes.");
    }

    [Fact]
    public async Task AppDeactivation_DoesNotNavigateCalendarWhileMainWindowIsInteractionBlocked()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "App.xaml.cs"));
        var methodStart = source.IndexOf("private bool ShouldSkipReturnToToday()", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private async Task CompleteStartupInitializationAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && nextMethod > methodStart);
        var method = source[methodStart..nextMethod];

        Assert.Contains("MainWindow?.IsEnabled == false", method, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreUi_RestartRequiredWarningDoesNotUseDisabledMainWindowAsMessageBoxOwner()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf("private async Task RestoreAllCalendarsAsync()", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private async Task ImportCsvAsync()", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && nextMethod > methodStart);
        var method = source[methodStart..nextMethod];

        var catchStart = method.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var restartCheck = method.IndexOf("_viewModel.IsDatabaseRestartRequired", catchStart, StringComparison.Ordinal);
        var restartTitle = method.IndexOf("リストア後の再起動が必要", catchStart, StringComparison.Ordinal);
        var ownerlessWarning = method.IndexOf("MessageBox.Show(\n                ex.Message", catchStart, StringComparison.Ordinal);
        var ownedFailure = method.IndexOf("MessageBox.Show(this, ex.Message, \"リストア失敗\"", catchStart, StringComparison.Ordinal);

        Assert.True(catchStart >= 0 && restartCheck > catchStart,
            "The restore UI must distinguish a completed DB restore that requires process restart from a true restore failure.");
        Assert.True(restartTitle > restartCheck && ownerlessWarning > restartCheck,
            "Restart-required warnings must be shown without the disabled MainWindow as owner.");
        Assert.True(ownedFailure > restartCheck,
            "Ordinary pre-replacement restore failures should retain the owned error dialog.");
    }
}
