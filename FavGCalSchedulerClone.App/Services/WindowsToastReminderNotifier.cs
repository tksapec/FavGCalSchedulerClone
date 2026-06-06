using Microsoft.Toolkit.Uwp.Notifications;

namespace FavGCalSchedulerClone.App.Services;

public sealed class WindowsToastReminderNotifier : IReminderNotifier, IReminderNotifierMetadata
{
    private readonly WindowsToastInitializationService _toastInitializationService;
    private readonly bool _toastVerified;

    public WindowsToastReminderNotifier(WindowsToastInitializationService toastInitializationService, bool toastVerified)
    {
        _toastInitializationService = toastInitializationService;
        _toastVerified = toastVerified;
    }

    public string DeliveryMethodName => "WindowsToast";
    public bool UsedMessageBoxFallback => false;
    public bool ToastVerified => _toastVerified;
    public string? ToastStatus => _toastInitializationService.CurrentStatus.ToDisplayText();

    public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
    {
        var status = _toastInitializationService.CurrentStatus;
        if (!status.IsReady)
        {
            throw new InvalidOperationException(status.ToDisplayText());
        }

        if (!_toastVerified)
        {
            throw new InvalidOperationException("Windows toast notification has not been verified by a successful test notification.");
        }

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
