namespace FavGCalSchedulerClone.Tests;

public sealed class BuildWorkflowTests
{
    private static readonly string WorkflowPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        ".github", "workflows", "build-test.yml"));

    [Fact]
    public async Task BuildWorkflow_RestoresBuildsTestsAndUploadsCoverageResultsOnWindows()
    {
        var workflow = await File.ReadAllTextAsync(WorkflowPath);

        Assert.Contains("runs-on: windows-latest", workflow);
        Assert.Contains("actions/setup-dotnet@v4", workflow);
        Assert.Contains("dotnet-version: 9.0.x", workflow);
        Assert.Contains("dotnet restore FavGCalSchedulerClone.sln", workflow);
        Assert.Contains("dotnet build FavGCalSchedulerClone.sln --configuration Release --no-restore", workflow);
        Assert.Contains("dotnet test FavGCalSchedulerClone.sln --configuration Release --no-build --collect:\"XPlat Code Coverage\"", workflow);
        Assert.Contains("actions/upload-artifact@v4", workflow);
        Assert.Contains("if: always()", workflow);
        Assert.Contains("path: TestResults", workflow);
    }
}
