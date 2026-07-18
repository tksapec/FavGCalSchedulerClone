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

        Assert.Contains("初回同期時は、既定で過去 5 年分の Google Calendar 予定を取得します。", readme);
    }

    [Fact]
    public async Task Readme_DocumentsJapaneseHolidayUpdatesAndIsoWeekNumbers()
    {
        var readme = await File.ReadAllTextAsync(ReadmePath);

        Assert.Contains("ISO 週番号", readme);
        Assert.Contains("祝日を更新", readme);
        Assert.Contains("内閣府", readme);
    }
}
