namespace FavGCalSchedulerClone.Tests;

public sealed class ReturnToTodayReadmeTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", ".."));
    private static readonly string ReadmePath = Path.Combine(RepositoryRoot, "README.md");
    private static readonly string TestingPath = Path.Combine(RepositoryRoot, "docs", "TESTING.md");

    [Fact]
    public async Task Documentation_DocumentsBothReturnToTodayModesAndQuickToggle()
    {
        var readme = await File.ReadAllTextAsync(ReadmePath);
        var testing = await File.ReadAllTextAsync(TestingPath);
        var documentation = $"{readme}\n{testing}";

        Assert.Contains("フォーカス解除時に今日へ戻す", readme);
        Assert.Contains("ONの場合", testing);
        Assert.Contains("OFFの場合", testing);
        Assert.Contains("アプリ設定を開かずに", testing);
        Assert.DoesNotContain("日付を移動しても選択が勝手に本日に戻らない", documentation);
        Assert.DoesNotContain("今日ボタンを押したときだけ本日に戻る", documentation);
    }
}
