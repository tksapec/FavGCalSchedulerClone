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
    public async Task Restore_AcquiresRepositoryMaintenanceLeaseBeforeReplacingDatabase()
    {
        var source = await ReadAppFileAsync("ViewModels", "MainViewModel.BackupRestore.cs");

        var leaseIndex = source.IndexOf("BeginMaintenanceAsync", StringComparison.Ordinal);
        var restoreIndex = source.IndexOf("RestoreBackupAsync", StringComparison.Ordinal);
        var initializeIndex = source.IndexOf("InitializeAsync", restoreIndex, StringComparison.Ordinal);
        Assert.True(leaseIndex >= 0 && restoreIndex > leaseIndex);
        Assert.True(initializeIndex > restoreIndex);
        Assert.Contains("EndMaintenance", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repository_RejectsNewConnectionsDuringDatabaseMaintenance()
    {
        var source = await ReadAppFileAsync("Services", "CalendarRepository.cs");

        Assert.Contains("BeginMaintenanceAsync", source, StringComparison.Ordinal);
        Assert.Contains("_databaseMaintenanceRequested", source, StringComparison.Ordinal);
        Assert.Contains("Database maintenance is in progress", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReminderLifetime_IsDisposedOnlyByTheDependencyInjectionContainer()
    {
        var mainWindow = await ReadAppFileAsync("MainWindow.xaml.cs");
        var app = await ReadAppFileAsync("App.xaml.cs");

        Assert.Contains("_reminderService.Stop();", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("_reminderService.Dispose();", mainWindow, StringComparison.Ordinal);
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
