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
