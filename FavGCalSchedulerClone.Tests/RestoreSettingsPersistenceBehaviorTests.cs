using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class RestoreSettingsPersistenceBehaviorTests
{
    [Fact]
    public async Task Restore_DrainsCapturedDisplayMonthSaveBeforeSwapAndAllowsPostRestoreSettingsSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"restore-settings-race-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var targetPath = Path.Combine(directory, "target.db");
        var backupPath = Path.Combine(directory, "backup.zip");
        var backupService = new BackupService();
        var restoredMonth = new DateTime(2026, 2, 1);

        var sourceRepository = new CalendarRepository(sourcePath);
        await sourceRepository.InitializeAsync();
        await sourceRepository.SaveSettingsAsync(new AppSettings
        {
            DisplayMonth = restoredMonth,
            WindowOpacity = 111
        });
        await backupService.CreateBackupAsync(sourcePath, backupPath);

        var targetRepository = new CalendarRepository(targetPath);
        await targetRepository.InitializeAsync();
        await targetRepository.SaveSettingsAsync(new AppSettings
        {
            DisplayMonth = new DateTime(2026, 8, 1),
            WindowOpacity = 222
        });
        var viewModel = new MainViewModel(
            targetRepository,
            new GoogleCalendarSyncService(targetRepository),
            backupService,
            new CalendarCsvService(),
            new FavGCalSchedulerImportService(targetRepository));
        await viewModel.InitializeAsync();

        var staleSaveCaptured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStaleSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.BeforeSaveDisplayMonthAsync = async _ =>
        {
            staleSaveCaptured.TrySetResult();
            await releaseStaleSave.Task.ConfigureAwait(false);
        };

        viewModel.NextMonthCommand.Execute(null);
        await staleSaveCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var restoreTask = viewModel.RestoreAllCalendarsAsync(backupPath);
        await Task.Delay(100);
        Assert.False(restoreTask.IsCompleted);

        releaseStaleSave.TrySetResult();
        await restoreTask.WaitAsync(TimeSpan.FromSeconds(10));

        var restored = await targetRepository.LoadSettingsAsync();
        Assert.Equal(111, restored.WindowOpacity);
        Assert.Equal(restoredMonth, restored.DisplayMonth);

        viewModel.BeforeSaveDisplayMonthAsync = null;
        var postRestoreSettings = viewModel.CreateSettingsSnapshot();
        postRestoreSettings.WindowOpacity = 77;
        await viewModel.SaveApplicationSettingsAsync(postRestoreSettings);

        var persistedAfterEdit = await targetRepository.LoadSettingsAsync();
        Assert.Equal(77, persistedAfterEdit.WindowOpacity);
        Assert.Equal(restoredMonth, persistedAfterEdit.DisplayMonth);
    }
}
