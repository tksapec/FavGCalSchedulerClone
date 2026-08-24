using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReminderMaintenancePauseTests
{
    [Fact]
    public async Task PauseForMaintenanceAsync_WaitsForInFlightReminderCheck()
    {
        var repository = await CreateRepositoryAsync();
        var notifier = new BlockingNotifier();
        using var service = new ReminderNotificationService(repository, notifier);
        var now = new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(9));
        await repository.SaveEventAsync(new CalendarEvent
        {
            Id = "maintenance-reminder",
            Title = "Maintenance reminder",
            Start = now.AddMinutes(5),
            End = now.AddHours(1),
            ReminderMinutesBeforeStart = 10,
            IsDirty = false
        });

        var checkTask = service.CheckDueRemindersAsync(now);
        await notifier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var pauseTask = service.PauseForMaintenanceAsync();
        await Task.Delay(50);
        Assert.False(pauseTask.IsCompleted);

        notifier.Release();
        await checkTask;
        var wasRunning = await pauseTask;

        Assert.False(wasRunning);
        Assert.False(service.IsRunning);
        await service.ResumeAfterMaintenanceAsync(wasRunning);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task ResumeAfterMaintenanceAsync_RestoresPriorMonitoringState()
    {
        var repository = await CreateRepositoryAsync();
        using var service = new ReminderNotificationService(repository, new RecordingNotifier());
        await service.StartAsync();

        var wasRunning = await service.PauseForMaintenanceAsync();

        Assert.True(wasRunning);
        Assert.False(service.IsRunning);

        await service.ResumeAfterMaintenanceAsync(wasRunning);

        Assert.True(service.IsRunning);
        service.Stop();
    }

    private static async Task<CalendarRepository> CreateRepositoryAsync()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        return repository;
    }

    private sealed class BlockingNotifier : IReminderNotifier
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered => _entered;

        public async Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class RecordingNotifier : IReminderNotifier
    {
        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
