using System.Reflection;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

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
            reminderService);
        await viewModel.InitializeAsync();

        await viewModel.RestoreAllCalendarsAsync(backupPath);

        Assert.True(reminderService.IsRunning);
        Assert.Equal(2, (await targetRepository.LoadSettingsAsync()).StartupTabIndex);
        reminderService.Stop();
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

    private sealed class RecordingNotifier : IReminderNotifier
    {
        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
