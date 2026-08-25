using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class FallbackReminderNotifierCancellationTests
{
    [Fact]
    public async Task ShowAsync_WhenPrimaryIsCanceled_DoesNotInvokeFallback()
    {
        var fallback = new RecordingNotifier();
        var notifier = new FallbackReminderNotifier(new CancelingNotifier(), fallback);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var notification = new ReminderNotification(
            "cancel-test",
            "event",
            "Canceled reminder",
            "2026/08/25 10:00",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            "primary",
            false);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => notifier.ShowAsync(notification, cancellation.Token));

        Assert.Equal(0, fallback.CallCount);
    }

    private sealed class CancelingNotifier : IReminderNotifier
    {
        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default) =>
            Task.FromCanceled(cancellationToken);
    }

    private sealed class RecordingNotifier : IReminderNotifier
    {
        public int CallCount { get; private set; }

        public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
