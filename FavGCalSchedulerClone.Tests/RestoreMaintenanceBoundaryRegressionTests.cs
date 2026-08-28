using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class RestoreMaintenanceBoundaryRegressionTests
{
    private static readonly string AppRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App"));

    [Fact]
    public async Task RestoreUi_DisablesMainWindowForEntireDatabaseMaintenanceWindow()
    {
        var maintenanceSource = await File.ReadAllTextAsync(Path.Combine(
            AppRoot, "ViewModels", "MainViewModel.Maintenance.cs"));
        var interactionPath = Path.Combine(AppRoot, "MainWindow.RestoreMaintenance.cs");

        Assert.True(File.Exists(interactionPath),
            "MainWindow must observe the ViewModel maintenance state without modifying the large primary window source file.");
        var interactionSource = await File.ReadAllTextAsync(interactionPath);

        Assert.Contains("public bool IsDatabaseMaintenanceInProgress", maintenanceSource, StringComparison.Ordinal);
        Assert.Contains("nameof(IsDatabaseMaintenanceInProgress)", maintenanceSource, StringComparison.Ordinal);
        Assert.Contains("nameof(MainViewModel.IsDatabaseMaintenanceInProgress)", interactionSource, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = false;", interactionSource, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = wasEnabled;", interactionSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackupAllCalendarsAsync_UsesSameExclusiveDataGateAsRestore()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "ViewModels", "MainViewModel.BackupRestore.cs"));
        var method = ExtractMethod(
            source,
            "public Task<BackupResult> BackupAllCalendarsAsync",
            "public async Task<RestoreResult> RestoreAllCalendarsAsync");

        Assert.Contains("RunExclusiveSyncDataOperationAsync", method, StringComparison.Ordinal);
        Assert.Contains("BackupAllCalendarsCoreAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreAllCalendarsAsync_MarksReminderForResumeBeforeAwaitingPause()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "ViewModels", "MainViewModel.BackupRestore.cs"));
        var method = ExtractMethod(
            source,
            "public async Task<RestoreResult> RestoreAllCalendarsAsync",
            "private void ResetTransientStateAfterDatabaseRestore");

        var markResumeRequired = method.IndexOf("reminderResumeRequired = true;", StringComparison.Ordinal);
        var pause = method.IndexOf("await _reminderService.PauseForMaintenanceAsync();", StringComparison.Ordinal);
        var resumeCheck = method.IndexOf("if (reminderResumeRequired)", StringComparison.Ordinal);
        var resume = method.IndexOf("await _reminderService!.ResumeAfterMaintenanceAsync(reminderWasRunning)", StringComparison.Ordinal);

        Assert.True(markResumeRequired >= 0 && pause > markResumeRequired,
            "Restore must mark reminder recovery as required before PauseForMaintenanceAsync can throw.");
        Assert.True(resumeCheck > pause && resume > resumeCheck,
            "Restore finally must always resume a reminder service whose pause was attempted.");
    }

    [Fact]
    public async Task RestoreAllCalendarsAsync_WhenReloadFails_ReportsDatabaseAlreadyRestored()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"restore-reload-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var targetPath = Path.Combine(directory, "calendar.db");
        var backupPath = Path.Combine(directory, "backup.zip");
        var backupService = new BackupService();

        try
        {
            var sourceRepository = new CalendarRepository(sourcePath);
            await sourceRepository.InitializeAsync();
            await sourceRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 4 });
            await backupService.CreateBackupAsync(sourcePath, backupPath);

            var targetRepository = new CalendarRepository(targetPath);
            await targetRepository.InitializeAsync();
            await targetRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 1 });
            var viewModel = new MainViewModel(targetRepository, new GoogleCalendarSyncService(targetRepository));
            await viewModel.InitializeAsync();
            viewModel.BeforeLoadCalendarSnapshotAsync = (_, _) =>
                throw new InvalidOperationException("forced reload failure");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                viewModel.RestoreAllCalendarsAsync(backupPath));

            Assert.Contains("DBの復元は完了", exception.Message, StringComparison.Ordinal);
            Assert.Contains("再起動", exception.Message, StringComparison.Ordinal);
            Assert.Equal(4, (await targetRepository.LoadSettingsAsync()).StartupTabIndex);
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
    public async Task RestoreAllCalendarsAsync_WhenReminderRestartFails_ReportsDatabaseAlreadyRestored()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"restore-reminder-restart-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var targetPath = Path.Combine(directory, "calendar.db");
        var reminderPath = Path.Combine(directory, "reminder.db");
        var backupPath = Path.Combine(directory, "backup.zip");
        var backupService = new BackupService();
        CalendarRepository? reminderRepository = null;
        ReminderNotificationService? reminderService = null;
        var reminderMaintenanceStarted = false;

        try
        {
            var sourceRepository = new CalendarRepository(sourcePath);
            await sourceRepository.InitializeAsync();
            await sourceRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 5 });
            await backupService.CreateBackupAsync(sourcePath, backupPath);

            var targetRepository = new CalendarRepository(targetPath);
            await targetRepository.InitializeAsync();
            await targetRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 1 });

            reminderRepository = new CalendarRepository(reminderPath);
            await reminderRepository.InitializeAsync();
            reminderService = new ReminderNotificationService(reminderRepository, new RecordingNotifier());
            await reminderService.StartAsync();

            var viewModel = new MainViewModel(
                targetRepository,
                new GoogleCalendarSyncService(targetRepository),
                backupService,
                new CalendarCsvService(),
                new FavGCalSchedulerImportService(targetRepository),
                logger: null,
                reminderService: reminderService);
            await viewModel.InitializeAsync();
            viewModel.BeforeLoadCalendarSnapshotAsync = async (_, _) =>
            {
                await reminderRepository.BeginMaintenanceAsync();
                reminderMaintenanceStarted = true;
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                viewModel.RestoreAllCalendarsAsync(backupPath));

            Assert.Contains("DBの復元は完了", exception.Message, StringComparison.Ordinal);
            Assert.Contains("通知監視", exception.Message, StringComparison.Ordinal);
            Assert.Contains("再起動", exception.Message, StringComparison.Ordinal);
            Assert.Equal(5, (await targetRepository.LoadSettingsAsync()).StartupTabIndex);
            Assert.False(viewModel.IsDatabaseMaintenanceInProgress);
        }
        finally
        {
            if (reminderMaintenanceStarted)
            {
                reminderRepository!.EndMaintenance();
            }
            reminderService?.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string ExtractMethod(string source, string startMarker, string nextMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{startMarker} was not found.");
        var end = source.IndexOf(nextMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"{nextMarker} was not found after {startMarker}.");
        return source[start..end];
    }

    private sealed class RecordingNotifier : IReminderNotifier
    {
        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
