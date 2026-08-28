using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class RestoreRestartRequiredLatchRegressionTests
{
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

            var syncException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                viewModel.SynchronizeDirtyOnlyAsync());
            Assert.Contains("再起動", syncException.Message, StringComparison.Ordinal);

            var backupException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                viewModel.BackupAllCalendarsAsync(postFailureBackupPath));
            Assert.Contains("再起動", backupException.Message, StringComparison.Ordinal);
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
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "FavGCalSchedulerClone.App", "ViewModels", "MainViewModel.BackupRestore.cs"));
        var source = await File.ReadAllTextAsync(sourcePath);
        var reloadCatch = source.IndexOf("catch (Exception ex)", source.IndexOf("ReloadRestoredViewModelStateAsync", StringComparison.Ordinal), StringComparison.Ordinal);
        var message = source.IndexOf("バックアップDBの復元は完了しましたが、画面状態の再読み込みに失敗しました", reloadCatch, StringComparison.Ordinal);
        var stopReminderRestart = source.IndexOf("reminderWasRunning = false;", reloadCatch, StringComparison.Ordinal);

        Assert.True(reloadCatch >= 0 && message > reloadCatch && stopReminderRestart > reloadCatch && stopReminderRestart < message,
            "A partial ViewModel reload failure must prevent reminder monitoring from being restarted against uncertain in-memory state.");
    }
}
