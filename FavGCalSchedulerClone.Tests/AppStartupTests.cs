using FavGCalSchedulerClone.App;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class AppStartupTests
{
    [Fact]
    public async Task RunStartupInitializationAsync_LogsInitializationFailure()
    {
        var logger = new CapturingLogger();

        var succeeded = await FavGCalSchedulerClone.App.App.RunStartupInitializationAsync(
            () => throw new InvalidOperationException("startup failed"),
            logger);

        Assert.False(succeeded);
        var entry = Assert.Single(logger.Errors);
        Assert.Equal("Application startup initialization failed.", entry.Context);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task RunStartupInitializationAsync_ReturnsTrueOnlyAfterSuccessfulInitialization()
    {
        var logger = new CapturingLogger();

        var succeeded = await FavGCalSchedulerClone.App.App.RunStartupInitializationAsync(
            () => Task.CompletedTask,
            logger);

        Assert.True(succeeded);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task App_StoresTheStartupOutcomeInsteadOfMarkingCompletionInFinally()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            GetRepositoryRoot(),
            "FavGCalSchedulerClone.App",
            "App.xaml.cs"));

        Assert.Contains(
            "_startupInitializationCompleted = await RunStartupInitializationAsync(initialize, logger);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("finally { _startupInitializationCompleted = true; }", source, StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FavGCalSchedulerClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class CapturingLogger : IAppLogger
    {
        public List<(Exception Exception, string Context)> Errors { get; } = [];

        public void LogError(Exception exception, string context)
        {
            Errors.Add((exception, context));
        }

        public void LogInfo(string message)
        {
        }
    }
}
