namespace FavGCalSchedulerClone.Tests;

public sealed class EventListBulkOperationIntegrationSourceTests
{
    [Fact]
    public void EventListDialog_UsesDetailedBulkResultsAndSharedOperationGate()
    {
        var source = ReadSource("FavGCalSchedulerClone.App", "Views", "Dialogs", "EventListDialog.cs");

        Assert.Contains("Task<BulkEventOperationResult>", source, StringComparison.Ordinal);
        Assert.Contains("var operationGate = new AsyncOperationGate();", source, StringComparison.Ordinal);
        Assert.Contains("operationGate.TryRunAsync", source, StringComparison.Ordinal);
        Assert.Contains("FormatStatus(\"一括編集\", \"変更\")", source, StringComparison.Ordinal);
        Assert.Contains("FormatStatus(\"一括削除\", \"削除\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EventListDialog_DisablesSearchAndRowEditingWhileBulkMutationRuns()
    {
        var source = ReadSource("FavGCalSchedulerClone.App", "Views", "Dialogs", "EventListDialog.cs");

        Assert.Contains("searchToolbar.IsEnabled = false;", source, StringComparison.Ordinal);
        Assert.Contains("grid.IsEnabled = false;", source, StringComparison.Ordinal);
        Assert.Contains("searchToolbar.IsEnabled = true;", source, StringComparison.Ordinal);
        Assert.Contains("grid.IsEnabled = true;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EventListDialog_PreventsClosingWhileBulkMutationRuns()
    {
        var source = ReadSource("FavGCalSchedulerClone.App", "Views", "Dialogs", "EventListDialog.cs");

        Assert.Contains("window.Closing +=", source, StringComparison.Ordinal);
        Assert.Contains("operationGate.IsRunning", source, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModel_PreservesCountReturningBulkApisAndExposesDetailedApis()
    {
        var source = ReadSource("FavGCalSchedulerClone.App", "ViewModels", "MainViewModel.BulkUndo.cs");

        Assert.Contains("Task<int> BulkUpdateEventsAsync", source, StringComparison.Ordinal);
        Assert.Contains("Task<int> BulkDeleteEventsAsync", source, StringComparison.Ordinal);
        Assert.Contains("Task<BulkEventOperationResult> BulkUpdateEventsDetailedAsync", source, StringComparison.Ordinal);
        Assert.Contains("Task<BulkEventOperationResult> BulkDeleteEventsDetailedAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_UsesDetailedBulkApisForEventListStatus()
    {
        var source = ReadSource("FavGCalSchedulerClone.App", "MainWindow.xaml.cs");

        Assert.Contains("BulkUpdateEventsDetailedAsync", source, StringComparison.Ordinal);
        Assert.Contains("BulkDeleteEventsDetailedAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BulkEventOperationResult_DoesNotUseImplicitCountCompatibilityConversion()
    {
        var source = ReadSource("FavGCalSchedulerClone.App", "Models", "BulkEventOperationResult.cs");

        Assert.DoesNotContain("implicit operator BulkEventOperationResult", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModel_BulkMutationsShareTheSyncDataOperationGate()
    {
        var source = ReadSource("FavGCalSchedulerClone.App", "ViewModels", "MainViewModel.BulkUndo.cs");
        const string guardedMutation = "RunExclusiveSyncDataOperationAsync(async () =>";

        Assert.True(
            CountOccurrences(source, guardedMutation) >= 2,
            "Bulk update and bulk delete must both serialize their read/write mutation phase with sync data operations.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ReadSource(params string[] relativePathParts)
        => File.ReadAllText(Path.Combine([GetRepositoryRoot(), .. relativePathParts]));

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FavGCalSchedulerClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
