namespace FavGCalSchedulerClone.Tests;

public sealed class ReadmeTests
{
    private static readonly string ReadmePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "README.md"));

    [Fact]
    public async Task Readme_DocumentsInitialGoogleSyncRange()
    {
        var readme = await File.ReadAllTextAsync(ReadmePath);

        Assert.Contains("初回同期では既定で過去5年分の予定を取得", readme);
    }

    [Fact]
    public async Task Readme_DocumentsJapaneseHolidayUpdatesAndIsoWeekNumbers()
    {
        var readme = await File.ReadAllTextAsync(ReadmePath);

        Assert.Contains("ISO週番号", readme);
        Assert.Contains("オンライン更新", readme);
        Assert.Contains("内閣府", readme);
    }
}
