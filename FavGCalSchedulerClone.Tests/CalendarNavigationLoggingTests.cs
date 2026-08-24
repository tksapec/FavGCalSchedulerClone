using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarNavigationLoggingTests
{
    [Fact]
    public async Task Initialize_LogsCalendarNavigationTimings()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(databasePath);
        await repository.InitializeAsync();
        var logger = new CapturingLogger();
        var viewModel = new MainViewModel(
            repository,
            new GoogleCalendarSyncService(repository),
            new BackupService(),
            new CalendarCsvService(),
            new FavGCalSchedulerImportService(repository),
            logger);

        await viewModel.InitializeAsync();

        Assert.Contains(logger.Messages, message =>
            message.StartsWith("Calendar navigation ", StringComparison.Ordinal)
            && message.Contains("cacheHit=", StringComparison.Ordinal)
            && message.Contains("applyUi=", StringComparison.Ordinal));
    }

    private sealed class CapturingLogger : IAppLogger
    {
        public List<string> Messages { get; } = [];

        public void LogError(Exception exception, string context)
        {
        }

        public void LogInfo(string message) => Messages.Add(message);
    }
}
