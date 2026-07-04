using FavGCalSchedulerClone.App;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class AppStartupTests
{
    [Fact]
    public async Task RunStartupInitializationAsync_LogsInitializationFailure()
    {
        var logger = new CapturingLogger();

        await FavGCalSchedulerClone.App.App.RunStartupInitializationAsync(
            () => throw new InvalidOperationException("startup failed"),
            logger);

        var entry = Assert.Single(logger.Errors);
        Assert.Equal("Application startup initialization failed.", entry.Context);
        Assert.IsType<InvalidOperationException>(entry.Exception);
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
