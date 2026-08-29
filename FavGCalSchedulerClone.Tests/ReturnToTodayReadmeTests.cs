namespace FavGCalSchedulerClone.Tests;

public sealed class ReturnToTodayReadmeTests
{
    private static readonly string ReadmePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "README.md"));

    [Fact]
    public async Task Readme_DocumentsBothReturnToTodayModesAndQuickToggle()
    {
        var readme = await File.ReadAllTextAsync(ReadmePath);

        Assert.Contains("フォーカス解除時に今日へ戻す", readme);
        Assert.Contains("ONの場合", readme);
        Assert.Contains("OFFの場合", readme);
        Assert.Contains("アプリ設定を開かずに", readme);
        Assert.DoesNotContain("日付を移動しても選択が勝手に本日に戻らない", readme);
        Assert.DoesNotContain("今日ボタンを押したときだけ本日に戻る", readme);
    }
}
