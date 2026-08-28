namespace FavGCalSchedulerClone.Tests;

public sealed class FinalReliabilitySourceTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", ".."));

    [Fact]
    public async Task LegacyImport_CommitsDatabaseChangesOnlyAfterAllPostProcessingSucceeds()
    {
        var source = await ReadAppFileAsync("Services", "FavGCalSchedulerImportService.cs");
        var importStart = source.IndexOf("public async Task<FavGCalImportResult> ImportAsync", StringComparison.Ordinal);
        var nextMember = source.IndexOf("public static string? ExtractCalendarIdFromFeedUrl", importStart, StringComparison.Ordinal);
        Assert.True(importStart >= 0 && nextMember > importStart);
        var importBody = source[importStart..nextMember];

        Assert.DoesNotContain("_repository.SaveEventAsync(", importBody, StringComparison.Ordinal);
        Assert.Contains("CalendarRepositoryAtomicWriter.SaveEventsAsync", importBody, StringComparison.Ordinal);
        var comparisonIndex = importBody.IndexOf("_compareService.Compare", StringComparison.Ordinal);
        var commitIndex = importBody.IndexOf("CalendarRepositoryAtomicWriter.SaveEventsAsync", StringComparison.Ordinal);
        Assert.True(comparisonIndex >= 0 && commitIndex > comparisonIndex);
    }

    [Fact]
    public async Task Restore_KeepsRepositoryMaintenanceLeaseThroughViewModelReinitialization()
    {
        var source = await ReadAppFileAsync("ViewModels", "MainViewModel.BackupRestore.cs");

        var leaseIndex = source.IndexOf("BeginMaintenanceAsync", StringComparison.Ordinal);
        var restoreIndex = source.IndexOf("RestoreBackupAsync", StringComparison.Ordinal);
        var initializeIndex = source.IndexOf("RunWithMaintenanceAccessAsync(ReloadRestoredViewModelStateAsync)", restoreIndex, StringComparison.Ordinal);
        var endMaintenanceIndex = source.IndexOf("EndMaintenance", restoreIndex, StringComparison.Ordinal);
        Assert.True(leaseIndex >= 0 && restoreIndex > leaseIndex);
        Assert.True(initializeIndex > restoreIndex);
        Assert.True(endMaintenanceIndex > initializeIndex);
    }

    [Fact]
    public async Task Restore_PreparesAndMigratesDatabaseBeforeMovingTheLiveDatabase()
    {
        var source = await ReadAppFileAsync("Services", "BackupService.cs");
        var restoreStart = source.IndexOf("public async Task<RestoreResult> RestoreBackupAsync", StringComparison.Ordinal);
        var validateArchiveStart = source.IndexOf("private static void ValidateArchive", restoreStart, StringComparison.Ordinal);
        Assert.True(restoreStart >= 0 && validateArchiveStart > restoreStart);
        var restoreBody = source[restoreStart..validateArchiveStart];

        var prepareIndex = restoreBody.IndexOf("PrepareRestoredDatabaseAsync(tempRestorePath, preparedRestorePath", StringComparison.Ordinal);
        var moveLiveIndex = restoreBody.IndexOf("MoveFileWithRetryAsync(databasePath, rollbackPath", StringComparison.Ordinal);
        var installPreparedIndex = restoreBody.IndexOf("MoveFileWithRetryAsync(preparedRestorePath, databasePath", StringComparison.Ordinal);

        Assert.True(prepareIndex >= 0 && moveLiveIndex > prepareIndex);
        Assert.True(installPreparedIndex > moveLiveIndex);
        Assert.DoesNotContain("MoveFileWithRetryAsync(tempRestorePath, databasePath", restoreBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repository_RejectsNewConnectionsDuringDatabaseMaintenanceAndAtomicWriterUsesTrackedConnection()
    {
        var repository = await ReadAppFileAsync("Services", "CalendarRepository.cs");
        var atomicWriter = await ReadAppFileAsync("Services", "CalendarRepositoryAtomicWriter.cs");

        Assert.Contains("BeginMaintenanceAsync", repository, StringComparison.Ordinal);
        Assert.Contains("RunWithMaintenanceAccessAsync", repository, StringComparison.Ordinal);
        Assert.Contains("_databaseMaintenanceRequested", repository, StringComparison.Ordinal);
        Assert.Contains("Database maintenance is in progress", repository, StringComparison.Ordinal);
        Assert.Contains("repository.OpenConnection()", atomicWriter, StringComparison.Ordinal);
        Assert.DoesNotContain("new SqliteConnection", atomicWriter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReminderLifetime_DoubleDisposeRemainsIdempotent()
    {
        var mainWindow = await ReadAppFileAsync("MainWindow.xaml.cs");
        var service = await ReadAppFileAsync("Services", "ReminderNotificationService.cs");
        var app = await ReadAppFileAsync("App.xaml.cs");

        Assert.Contains("_reminderService.Stop();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_reminderService.Dispose();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("if (_disposed)", service, StringComparison.Ordinal);
        Assert.Contains("_disposed = true;", service, StringComparison.Ordinal);
        Assert.Contains("_serviceProvider?.Dispose();", app, StringComparison.Ordinal);
    }

    private static Task<string> ReadAppFileAsync(params string[] relativePath)
    {
        var path = relativePath.Aggregate(
            Path.Combine(Root, "FavGCalSchedulerClone.App"),
            Path.Combine);
        return File.ReadAllTextAsync(path);
    }
}
