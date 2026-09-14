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
    public void MainViewModel_ExposesBulkOperationResultFromCanonicalApis()
    {
        var source = ReadSource("FavGCalSchedulerClone.App", "ViewModels", "MainViewModel.BulkUndo.cs");

        Assert.Contains("Task<BulkEventOperationResult> BulkUpdateEventsAsync", source, StringComparison.Ordinal);
        Assert.Contains("Task<BulkEventOperationResult> BulkDeleteEventsAsync", source, StringComparison.Ordinal);
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
