namespace FavGCalSchedulerClone.Tests;

public sealed class PublishReleaseScriptTests
{
    private static readonly string ScriptPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "scripts", "publish-release.ps1"));

    [Fact]
    public async Task PublishScript_StagesSuccessfulOutputBeforeReplacingPublishedRelease()
    {
        var script = await File.ReadAllTextAsync(ScriptPath);

        Assert.Contains("$stagingPublishDirectory", script);
        Assert.Contains("$stagingPublishDirectory = Join-Path $repositoryRoot", script);
        Assert.Contains("-o $stagingPublishDirectory", script);
        Assert.DoesNotContain("-o $publishDirectory", script);
        Assert.Contains("[System.IO.Directory]::Move($stagingPublishDirectory, $publishDirectory)", script);

        var publishExitCheck = script.IndexOf("Publish failed with exit code", StringComparison.Ordinal);
        var deletePublished = script.IndexOf("[System.IO.Directory]::Delete($publishDirectory, $true)", StringComparison.Ordinal);
        var moveStaged = script.IndexOf("[System.IO.Directory]::Move($stagingPublishDirectory, $publishDirectory)", StringComparison.Ordinal);
        Assert.True(publishExitCheck >= 0);
        Assert.True(deletePublished > publishExitCheck);
        Assert.True(moveStaged > deletePublished);
    }

    [Fact]
    public async Task PublishScript_ExcludesProjectBinAndObjWhenIntermediateOutputIsRedirected()
    {
        var script = await File.ReadAllTextAsync(ScriptPath);

        Assert.Contains("'-p:DefaultItemExcludes=**/obj/**'", script);
    }
}
