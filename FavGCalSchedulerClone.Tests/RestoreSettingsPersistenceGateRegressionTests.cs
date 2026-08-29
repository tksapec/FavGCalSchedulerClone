using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class RestoreSettingsPersistenceGateRegressionTests
{
    private static readonly string BackupRestoreSourcePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App", "ViewModels", "MainViewModel.BackupRestore.cs"));

    [Fact]
    public async Task Restore_AcquiresPersistenceGatesBeforeDatabaseReplacementAndReleasesInReverseOrder()
    {
        var source = await File.ReadAllTextAsync(BackupRestoreSourcePath);
        var method = ExtractMethod(
            source,
            "public async Task<RestoreResult> RestoreAllCalendarsAsync",
            "private async Task ReloadRestoredViewModelStateAsync");

        var syncWait = method.IndexOf("await _syncDataOperationGate.WaitAsync();", StringComparison.Ordinal);
        var displayWait = method.IndexOf("await _displayMonthPersistenceGate.WaitAsync();", StringComparison.Ordinal);
        var settingsWait = method.IndexOf("await _settingsPersistenceGate.WaitAsync();", StringComparison.Ordinal);
        var beginMaintenance = method.IndexOf("await _repository.BeginMaintenanceAsync();", StringComparison.Ordinal);
        var settingsRelease = method.LastIndexOf("_settingsPersistenceGate.Release();", StringComparison.Ordinal);
        var displayRelease = method.LastIndexOf("_displayMonthPersistenceGate.Release();", StringComparison.Ordinal);
        var syncRelease = method.LastIndexOf("_syncDataOperationGate.Release();", StringComparison.Ordinal);

        Assert.True(syncWait >= 0
                    && displayWait > syncWait
                    && settingsWait > displayWait
                    && beginMaintenance > settingsWait,
            "Restore must serialize pre-existing display-month/settings persistence before replacing the database.");
        Assert.True(settingsRelease > beginMaintenance
                    && displayRelease > settingsRelease
                    && syncRelease > displayRelease,
            "Restore must release settings, display-month, then sync gates in reverse acquisition order.");
    }

    [Fact]
    public async Task Restore_MarksPreRestoreSettingsRevisionsAsSupersededBeforePersistenceGatesAreReleased()
    {
        var source = await File.ReadAllTextAsync(BackupRestoreSourcePath);
        var method = ExtractMethod(
            source,
            "public async Task<RestoreResult> RestoreAllCalendarsAsync",
            "private async Task ReloadRestoredViewModelStateAsync");

        var reload = method.IndexOf("await _repository.RunWithMaintenanceAccessAsync(ReloadRestoredViewModelStateAsync);", StringComparison.Ordinal);
        var markBaseline = method.IndexOf("MarkRestoredSettingsPersistenceBaseline();", StringComparison.Ordinal);
        var settingsRelease = method.LastIndexOf("_settingsPersistenceGate.Release();", StringComparison.Ordinal);

        Assert.True(reload >= 0 && markBaseline > reload && settingsRelease > markBaseline,
            "Queued pre-restore settings snapshots must be invalidated after restored settings are loaded and before persistence resumes.");
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
