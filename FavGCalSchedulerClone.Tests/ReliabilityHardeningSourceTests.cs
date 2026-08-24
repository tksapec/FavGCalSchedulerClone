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
        Assert.Contains("rollbackPath, databasePath, overwrite: false, CancellationToken.None", source);
        Assert.Contains("rollbackWalPath, databaseWalPath, overwrite: false, CancellationToken.None", source);
        Assert.Contains("rollbackShmPath, databaseShmPath, overwrite: false, CancellationToken.None", source);
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
    public async Task Undo_IsConsumedOnlyAfterTheTransactionalRestoreSucceeds()
    {
        var source = await ReadAppFileAsync("ViewModels", "MainViewModel.BulkUndo.cs");

        var peekIndex = source.IndexOf("_undoService.Peek()", StringComparison.Ordinal);
        var writeIndex = source.IndexOf("CalendarRepositoryAtomicWriter.SaveEventsAsync(_repository, writes, hardDeleteIds)", StringComparison.Ordinal);
        var consumeIndex = source.IndexOf("_undoService.Consume(operation)", StringComparison.Ordinal);
        Assert.True(peekIndex >= 0);
        Assert.True(writeIndex > peekIndex);
        Assert.True(consumeIndex > writeIndex);
        Assert.Contains("operation.CreatedEventIds.Reverse()", source);
    }

    [Fact]
    public async Task SyncedCreatedOccurrenceUndo_RestoresMasterValuesInsteadOfDeletingTheOccurrence()
    {
        var source = await ReadAppFileAsync("ViewModels", "MainViewModel.BulkUndo.cs");

        Assert.Contains("IsSyncedEditedOccurrence", source);
        Assert.Contains("CreateRestoredOccurrenceFromMaster", source);
        Assert.Contains("restored.GoogleEventId = current.GoogleEventId", source);
        Assert.Contains("restored.Start = originalStart", source);
        Assert.Contains("restored.IsDeleted = false", source);
    }

    [Fact]
    public async Task RecurrenceMutations_RecordCreatedRowsForUndoAndKeepCalendarsAligned()
    {
        var source = await ReadAppFileAsync("ViewModels", "MainViewModel.Recurrence.cs");

        Assert.Contains("createdIds.Add(future.Id)", source);
        Assert.Contains("createdIds.Add(moved.Id)", source);
        Assert.Contains("return created ? [candidate.Id] : []", source);
        Assert.Contains("return [tombstone.Id]", source);
        Assert.Contains("LoadRecurrenceUndoSnapshotsAsync", source);
        Assert.Contains("moved.CalendarId = future.CalendarId", source);
        Assert.Contains("movedChild.CalendarId = target.CalendarId", source);
        Assert.Contains("targetCalendarId: candidate.CalendarId", source);
    }

    [Fact]
    public async Task CsvExport_NeutralizesSpreadsheetFormulaPrefixesIncludingFullWidthForms()
    {
        var source = await ReadAppFileAsync("Services", "CsvCellSanitizer.cs");

        Assert.Contains("IsFormulaPrefix", source);
        Assert.Contains("＝", source);
        Assert.Contains("＋", source);
        Assert.Contains("－", source);
        Assert.Contains("＠", source);
    }

    private static Task<string> ReadAppFileAsync(params string[] relativePath)
    {
        var path = relativePath.Aggregate(
            Path.Combine(Root, "FavGCalSchedulerClone.App"),
            Path.Combine);
        return File.ReadAllTextAsync(path);
    }
}
