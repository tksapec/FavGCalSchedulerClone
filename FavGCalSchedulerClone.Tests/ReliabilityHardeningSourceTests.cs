namespace FavGCalSchedulerClone.Tests;

public sealed class ReliabilityHardeningSourceTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", ".."));

    [Fact]
    public async Task Restore_MovesSqliteSidecarsWithTheRollbackDatabase()
    {
        var source = await ReadAppFileAsync("Services", "BackupService.cs");

        Assert.Contains("databasePath + \"-wal\"", source);
        Assert.Contains("rollbackPath + \"-wal\"", source);
        Assert.Contains("databasePath + \"-shm\"", source);
        Assert.Contains("rollbackPath + \"-shm\"", source);
    }

    [Fact]
    public async Task ReminderSound_IsMarshalledToTheWpfDispatcher()
    {
        var source = await ReadAppFileAsync("Services", "SoundReminderNotifier.cs");

        Assert.Contains("Dispatcher", source);
        Assert.Contains("CheckAccess()", source);
        Assert.Contains("InvokeAsync", source);
    }

    [Fact]
    public async Task AutomaticSyncTimer_HasAnExceptionBoundaryAndDoesNotOwnReminderDisposal()
    {
        var source = await ReadAppFileAsync("Services", "ApplicationStartupService.cs");

        Assert.Contains("AutomaticSyncTimer_Tick", source);
        Assert.Contains("catch (Exception ex)", source);
        Assert.DoesNotContain("_reminderService.Dispose();", source);
    }

    [Fact]
    public async Task SynchronizeFinally_UsesSafeDiagnosticsRefresh()
    {
        var source = await ReadAppFileAsync("ViewModels", "MainViewModel.Sync.cs");

        Assert.Contains("RefreshOperationalStatusSafelyAsync", source);
        Assert.Contains("RecordFailedSyncSafelyAsync", source);
    }

    [Fact]
    public async Task DialogAsyncHandlers_UseTheCommonExceptionGuard()
    {
        var guard = await ReadAppFileAsync("Views", "Dialogs", "DialogAsyncGuard.cs");
        var eventList = await ReadAppFileAsync("Views", "Dialogs", "EventListDialog.cs");
        var reminderHistory = await ReadAppFileAsync("Views", "Dialogs", "ReminderHistoryDialog.cs");
        var settings = await ReadAppFileAsync("Views", "Dialogs", "SettingsDialog.cs");

        Assert.Contains("catch (Exception ex)", guard);
        Assert.Contains("DialogAsyncGuard.Run", eventList);
        Assert.Contains("DialogAsyncGuard.Run", reminderHistory);
        Assert.Contains("DialogAsyncGuard.Run", settings);
    }

    [Fact]
    public async Task MonthDrag_RequiresThePointerToRemainOnThePressedSegment()
    {
        var source = await ReadAppFileAsync("MainWindow.MonthEventLayer.cs");

        Assert.Contains("ReferenceEquals(layer.HitTestSegment(e.GetPosition(layer)), segment)", source);
    }

    [Fact]
    public async Task NativeSqliteBundle_IsPinnedAboveTheWalResetFix()
    {
        var project = await File.ReadAllTextAsync(Path.Combine(Root, "FavGCalSchedulerClone.App", "FavGCalSchedulerClone.App.csproj"));

        Assert.Contains("SQLitePCLRaw.bundle_e_sqlite3", project);
        Assert.Contains("Version=\"3.0.5\"", project);
    }

    [Fact]
    public async Task MultiEventWrites_HaveATransactionalWriter()
    {
        var source = await ReadAppFileAsync("Services", "CalendarRepositoryAtomicWriter.cs");

        Assert.Contains("BeginTransaction", source);
        Assert.Contains("CommitAsync", source);
        Assert.Contains("RollbackAsync", source);
    }

    [Fact]
    public async Task CsvExport_NeutralizesSpreadsheetFormulaPrefixes()
    {
        var source = await ReadAppFileAsync("Services", "CsvCellSanitizer.cs");

        Assert.Contains("IsFormulaPrefix", source);
        Assert.Contains("'", source);
    }

    private static Task<string> ReadAppFileAsync(params string[] relativePath)
    {
        var path = relativePath.Aggregate(
            Path.Combine(Root, "FavGCalSchedulerClone.App"),
            Path.Combine);
        return File.ReadAllTextAsync(path);
    }
}
