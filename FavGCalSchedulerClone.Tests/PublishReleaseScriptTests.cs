namespace FavGCalSchedulerClone.Tests;

public sealed class PublishReleaseScriptTests
{
    private static readonly string ScriptPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "scripts", "publish-release.ps1"));

    [Fact]
    public async Task PublishScript_RemovesExistingPublishDirectoryBeforePublishing()
    {
        var script = await File.ReadAllTextAsync(ScriptPath);

        Assert.Contains("Test-Path -LiteralPath $publishDirectory", script);
        Assert.Contains("[System.IO.Directory]::Delete($publishDirectory, $true)", script);
        Assert.Contains("-o $publishDirectory", script);
    }
}
