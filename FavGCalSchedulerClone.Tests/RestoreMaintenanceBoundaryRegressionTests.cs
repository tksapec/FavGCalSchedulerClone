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
    public async Task RestoreUi_DisablesMainWindowAroundAwaitAndReenablesBeforeDialogs()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        var method = ExtractMethod(
            source,
            "private async Task RestoreAllCalendarsAsync()",
            "private async Task ImportCsvAsync()");

        var rememberEnabled = method.IndexOf("var wasEnabled = IsEnabled;", StringComparison.Ordinal);
        var disable = method.IndexOf("IsEnabled = false;", StringComparison.Ordinal);
        var restore = method.IndexOf("await _viewModel.RestoreAllCalendarsAsync(dialog.FileName);", StringComparison.Ordinal);
        var finallyIndex = method.IndexOf("finally", restore, StringComparison.Ordinal);
        var reenable = method.IndexOf("IsEnabled = wasEnabled;", finallyIndex, StringComparison.Ordinal);
        var successMessage = method.IndexOf("\"リストア完了\"", StringComparison.Ordinal);
        var failureMessage = method.IndexOf("\"リストア失敗\"", StringComparison.Ordinal);

        Assert.True(rememberEnabled >= 0
                    && disable > rememberEnabled
                    && restore > disable
                    && finallyIndex > restore
                    && reenable > finallyIndex,
            "MainWindow input must stay disabled for the awaited database restore and be restored in finally.");
        Assert.True(successMessage > reenable && failureMessage > reenable,
            "Completion and error dialogs must be shown only after the owner window is enabled again.");
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

    private static string ExtractMethod(string source, string startMarker, string nextMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{startMarker} was not found.");
        var end = source.IndexOf(nextMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"{nextMarker} was not found after {startMarker}.");
        return source[start..end];
    }
}
