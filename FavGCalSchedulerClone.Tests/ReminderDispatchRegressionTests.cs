using FavGCalSchedulerClone.App.Services;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReminderDispatchRegressionTests
{
    [Fact]
    public async Task ReminderTriggered_AwaitsEveryAsyncSubscriber()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            using var service = new ReminderNotificationService(repository);
            var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            service.ReminderTriggered += async _ =>
            {
                firstEntered.TrySetResult(true);
                await releaseFirst.Task;
            };
            service.ReminderTriggered += _ => Task.CompletedTask;

            var dispatch = service.ShowTestNotificationAsync();
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(50);

            Assert.False(dispatch.IsCompleted,
                "Notification dispatch must await every async ReminderTriggered subscriber, not only the last delegate task.");

            releaseFirst.TrySetResult(true);
            Assert.True(await dispatch.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
        }
    }

    [Fact]
    public async Task ShowTestNotificationAsync_PropagatesRequestedCancellation()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            using var service = new ReminderNotificationService(repository);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.ShowTestNotificationAsync(new CancellingNotifier(), cancellation.Token));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
        }
    }

    [Fact]
    public void Stop_AfterDispose_IsHarmless()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            var service = new ReminderNotificationService(repository);
            service.Dispose();

            var exception = Record.Exception(service.Stop);

            Assert.Null(exception);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
        }
    }

    [Fact]
    public async Task StartAsync_AfterDispose_IsHarmless()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            var service = new ReminderNotificationService(repository);
            service.Dispose();

            var exception = await Record.ExceptionAsync(service.StartAsync);

            Assert.Null(exception);
            Assert.False(service.IsRunning);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
        }
    }

    private sealed class CancellingNotifier : IReminderNotifier
    {
        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
