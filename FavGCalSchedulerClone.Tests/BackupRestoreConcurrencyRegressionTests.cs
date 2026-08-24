using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class BackupRestoreConcurrencyRegressionTests
{
    [Fact]
    public async Task RestoreAllCalendarsAsync_RejectsRestoreWhileGoogleSyncIsRunning()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        var syncField = typeof(MainViewModel).GetField("_syncInProgress", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(syncField);
        syncField!.SetValue(viewModel, 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.RestoreAllCalendarsAsync(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.zip")));

        Assert.Contains("同期", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreAllCalendarsAsync_PausesReminderDatabaseWorkAndRestoresMonitoringState()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"restore-maintenance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var targetPath = Path.Combine(directory, "calendar.db");
        var backupPath = Path.Combine(directory, "backup.zip");
        var backupService = new BackupService();

        var sourceRepository = new CalendarRepository(sourcePath);
        await sourceRepository.InitializeAsync();
        await sourceRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 2 });
        await backupService.CreateBackupAsync(sourcePath, backupPath);

        var targetRepository = new CalendarRepository(targetPath);
        await targetRepository.InitializeAsync();
        await targetRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 1 });
        using var reminderService = new ReminderNotificationService(targetRepository, new RecordingNotifier());
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

        await viewModel.RestoreAllCalendarsAsync(backupPath);

        Assert.True(reminderService.IsRunning);
        Assert.Equal(2, (await targetRepository.LoadSettingsAsync()).StartupTabIndex);
        reminderService.Stop();
    }

    [Fact]
    public async Task RestoreAllCalendarsAsync_KeepsUnrelatedRepositoryAccessBlockedDuringViewModelReload()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"restore-reload-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var targetPath = Path.Combine(directory, "calendar.db");
        var backupPath = Path.Combine(directory, "backup.zip");
        var backupService = new BackupService();
        var continueReload = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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

            var reloadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            viewModel.BeforeLoadCalendarSnapshotAsync = async (_, cancellationToken) =>
            {
                reloadStarted.TrySetResult(true);
                await continueReload.Task.WaitAsync(cancellationToken);
            };

            var restoreTask = viewModel.RestoreAllCalendarsAsync(backupPath);
            await reloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            await Assert.ThrowsAsync<InvalidOperationException>(() => targetRepository.LoadSettingsAsync());

            continueReload.TrySetResult(true);
            await restoreTask;
            Assert.Equal(4, (await targetRepository.LoadSettingsAsync()).StartupTabIndex);
        }
        finally
        {
            continueReload.TrySetResult(true);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RestoreAllCalendarsAsync_WhenPreflightFails_ResumesReminderAndLeavesCurrentDatabaseUsable()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"restore-preflight-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var invalidDatabasePath = Path.Combine(directory, "invalid.db");
        var targetPath = Path.Combine(directory, "calendar.db");
        var backupPath = Path.Combine(directory, "backup.zip");
        var backupService = new BackupService();

        try
        {
            await CreateMigrationFailureBackupAsync(invalidDatabasePath, backupPath);

            var targetRepository = new CalendarRepository(targetPath);
            await targetRepository.InitializeAsync();
            await targetRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 6 });
            using var reminderService = new ReminderNotificationService(targetRepository, new RecordingNotifier());
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

            await Assert.ThrowsAnyAsync<Exception>(() => viewModel.RestoreAllCalendarsAsync(backupPath));

            Assert.True(reminderService.IsRunning);
            Assert.Equal(6, (await targetRepository.LoadSettingsAsync()).StartupTabIndex);
            var maintenanceField = typeof(MainViewModel).GetField(
                "_databaseMaintenanceInProgress",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(maintenanceField);
            Assert.Equal(0, Assert.IsType<int>(maintenanceField!.GetValue(viewModel)));
            reminderService.Stop();
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
    public async Task RestoreImplementation_UsesReminderPauseAndFinallyResume()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "FavGCalSchedulerClone.App", "ViewModels", "MainViewModel.BackupRestore.cs"));
        var source = await File.ReadAllTextAsync(sourcePath);

        Assert.Contains("await _reminderService.PauseForMaintenanceAsync()", source);
        Assert.Contains("finally", source);
        Assert.Contains("await _reminderService!.ResumeAfterMaintenanceAsync(reminderWasRunning)", source);
    }

    private static async Task CreateMigrationFailureBackupAsync(string databasePath, string backupPath)
    {
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE events (id TEXT PRIMARY KEY);
                CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE tags (name TEXT PRIMARY KEY, color TEXT NOT NULL, is_visible INTEGER NOT NULL, priority INTEGER NOT NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
        using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
        var databaseEntry = archive.CreateEntry(BackupService.DatabaseEntryName);
        await using (var source = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var destination = databaseEntry.Open())
        {
            await source.CopyToAsync(destination);
        }

        var manifestEntry = archive.CreateEntry(BackupService.ManifestEntryName);
        await using var manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(
            manifestStream,
            new BackupManifest("FavGCalSchedulerClone", BackupService.FormatVersion, DateTimeOffset.Now, Path.GetFileName(databasePath)));
    }

    private sealed class RecordingNotifier : IReminderNotifier
    {
        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
