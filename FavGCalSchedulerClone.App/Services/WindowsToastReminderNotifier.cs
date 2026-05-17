using Microsoft.Toolkit.Uwp.Notifications;

namespace FavGCalSchedulerClone.App.Services;

public sealed class WindowsToastReminderNotifier : IReminderNotifier
{
    public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
    {
        var kind = notification.IsTodoLike ? "ToDo" : "予定";
        new ToastContentBuilder()
            .AddArgument("action", "open")
            .AddArgument("occurrenceKey", notification.OccurrenceKey)
            .AddText($"FavGCalSchedulerClone - {kind}")
            .AddText(notification.Title)
            .AddText(notification.DateDisplayText)
            .AddButton(new ToastButton()
                .SetContent("5分後")
                .AddArgument("action", "snooze")
                .AddArgument("minutes", "5")
                .AddArgument("occurrenceKey", notification.OccurrenceKey))
            .AddButton(new ToastButton()
                .SetContent("10分後")
                .AddArgument("action", "snooze")
                .AddArgument("minutes", "10")
                .AddArgument("occurrenceKey", notification.OccurrenceKey))
            .Show();

        return Task.CompletedTask;
    }
}
