using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class FileAppLoggerTests
{
    [Fact]
    public void LogError_WritesExceptionDetailsToDatedLogFile()
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logger = new FileAppLogger(
            logDirectory,
            () => new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.FromHours(9)));

        logger.LogError(new InvalidOperationException("boom"), "unit test context");

        var logPath = Path.Combine(logDirectory, "app-2026-07-04.log");
        Assert.True(File.Exists(logPath));
        var text = File.ReadAllText(logPath);
        Assert.Contains("unit test context", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void LogInfo_RemovesLogsOlderThanThirtyDays()
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDirectory);
        var oldLog = Path.Combine(logDirectory, "app-2026-06-03.log");
        var recentLog = Path.Combine(logDirectory, "app-2026-06-04.log");
        File.WriteAllText(oldLog, "old");
        File.WriteAllText(recentLog, "recent");
        var logger = new FileAppLogger(
            logDirectory,
            () => new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.FromHours(9)));

        logger.LogInfo("hello");

        Assert.False(File.Exists(oldLog));
        Assert.True(File.Exists(recentLog));
        Assert.True(File.Exists(Path.Combine(logDirectory, "app-2026-07-04.log")));
    }
}
