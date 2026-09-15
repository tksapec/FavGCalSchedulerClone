namespace FavGCalSchedulerClone.Tests;

public sealed class EventListSearchConcurrencyRegressionTests
{
    [Fact]
    public async Task EventListSearch_OnlyLatestSuccessfulRequestMayCommitResultsAndFilter()
    {
        var source = await File.ReadAllTextAsync(SourcePath());

        Assert.Contains("var searchGeneration = 0L;", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Increment(ref searchGeneration)", source, StringComparison.Ordinal);
        Assert.Contains("generation != Volatile.Read(ref searchGeneration)", source, StringComparison.Ordinal);

        var reloadIndex = source.IndexOf("var refreshed = await request.ReloadEventsAsync(nextFilter);", StringComparison.Ordinal);
        var commitIndex = source.IndexOf("updateCurrentFilter(nextFilter);", StringComparison.Ordinal);
        Assert.True(reloadIndex >= 0, "The search must load results before committing the filter.");
        Assert.True(commitIndex > reloadIndex, "The filter must be committed only after the latest search loads successfully.");
    }

    [Fact]
    public async Task EventListSearch_IgnoresFailuresFromSupersededRequests()
    {
        var source = await File.ReadAllTextAsync(SourcePath());

        Assert.Contains("catch when (generation != Volatile.Read(ref searchGeneration))", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventListMutations_InvalidatePendingSearchBeforeChangingTheList()
    {
        var source = await File.ReadAllTextAsync(SourcePath());

        Assert.Contains("Action invalidatePendingSearch", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, "invalidatePendingSearch();") >= 2,
            "Both row editing and bulk mutations must invalidate an older in-flight search before they can reload the list.");
    }

    [Fact]
    public async Task EventListClose_InvalidatesPendingSearchBeforeWindowIsDisposed()
    {
        var source = await File.ReadAllTextAsync(SourcePath());
        var callbackIndex = source.IndexOf("var invalidatePendingSearch = searchToolbarState.InvalidatePendingSearch;", StringComparison.Ordinal);
        Assert.True(callbackIndex >= 0);
        var afterCallback = source[callbackIndex..];

        Assert.Contains("window.Closing += (_, _) => invalidatePendingSearch();", afterCallback, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkEditAndDelete_InvalidatePendingSearchBeforeOpeningConfirmationUi()
    {
        var source = await File.ReadAllTextAsync(SourcePath());
        var bulkEditStart = source.IndexOf("bulkEdit.Click +=", StringComparison.Ordinal);
        var bulkDeleteStart = source.IndexOf("bulkDelete.Click +=", StringComparison.Ordinal);
        Assert.True(bulkEditStart >= 0 && bulkDeleteStart > bulkEditStart);

        var bulkEditHandler = source[bulkEditStart..bulkDeleteStart];
        var editInvalidate = bulkEditHandler.IndexOf("invalidatePendingSearch();", StringComparison.Ordinal);
        var editDialog = bulkEditHandler.IndexOf("BulkEventUpdateDialog.Show", StringComparison.Ordinal);
        Assert.True(editInvalidate >= 0 && editDialog > editInvalidate,
            "An in-flight search must be invalidated before the bulk-edit modal opens, otherwise it can replace the visible selection while the modal is open.");

        var bulkDeleteHandler = source[bulkDeleteStart..];
        var deleteInvalidate = bulkDeleteHandler.IndexOf("invalidatePendingSearch();", StringComparison.Ordinal);
        var deletePrompt = bulkDeleteHandler.IndexOf("MessageBox.Show", StringComparison.Ordinal);
        Assert.True(deleteInvalidate >= 0 && deletePrompt > deleteInvalidate,
            "An in-flight search must be invalidated before the bulk-delete confirmation opens, otherwise the captured IDs can diverge from the visible selection.");
    }

    [Fact]
    public async Task CustomSearchRange_NormalizesReversedDatesBeforeBuildingFilter()
    {
        var source = await File.ReadAllTextAsync(SourcePath());
        var createFilterStart = source.IndexOf("private static EventListFilter CreateFilter", StringComparison.Ordinal);
        var addColumnsStart = source.IndexOf("private static void AddColumns", createFilterStart, StringComparison.Ordinal);
        Assert.True(createFilterStart >= 0 && addColumnsStart > createFilterStart);
        var createFilter = source[createFilterStart..addColumnsStart];

        Assert.Contains("if (selectedEnd < selectedStart)", createFilter, StringComparison.Ordinal);
        Assert.Contains("(selectedStart, selectedEnd) = (selectedEnd, selectedStart);", createFilter, StringComparison.Ordinal);
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

    private static string SourcePath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App",
        "Views",
        "Dialogs",
        "EventListDialog.cs"));
}
