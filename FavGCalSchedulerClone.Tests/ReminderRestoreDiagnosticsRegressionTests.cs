using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReminderRestoreDiagnosticsRegressionTests
{
    [Fact]
    public async Task ResumeAfterMaintenance_WhenMonitoringStaysStopped_ReloadsOlderRestoredDiagnostics()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        using var service = new ReminderNotificationService(repository, new RecordingNotifier());
        var newerCheck = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.FromHours(9));
        var restoredCheck = newerCheck.AddDays(-30);

        await service.CheckDueRemindersDetailedAsync(newerCheck);
        Assert.Equal(newerCheck, service.CurrentDiagnostics.LastCheckAt);

        var wasRunning = await service.PauseForMaintenanceAsync();
        Assert.False(wasRunning);

        var restored = new ReminderMonitoringSnapshot(
            false, null, restoredCheck, null,
            3, 3, 1, 2, 3, 0, 0, 0, 0, 0,
            "restored diagnostic", []);
        await repository.SaveSettingValueAsync("reminder:diagnostics", JsonSerializer.Serialize(restored));

        await service.ResumeAfterMaintenanceAsync(resumeMonitoring: false);

        Assert.False(service.IsRunning);
        Assert.Equal(restoredCheck, service.CurrentDiagnostics.LastCheckAt);
        Assert.Equal("restored diagnostic", service.CurrentDiagnostics.LastError);
    }

    private sealed class RecordingNotifier : IReminderNotifier
    {
        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
