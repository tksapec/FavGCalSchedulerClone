using System.Windows;

namespace FavGCalSchedulerClone.App.Services;

public sealed class MessageBoxReminderNotifier : IReminderNotifier, IReminderNotifierMetadata
{
    private readonly Window _owner;

    public MessageBoxReminderNotifier(Window owner)
    {
        _owner = owner;
    }

    public string DeliveryMethodName => "MessageBox";
    public bool UsedMessageBoxFallback => false;
    public MessageBoxNotificationRole MessageBoxRole => MessageBoxNotificationRole.Primary;
    public bool ToastVerified => false;
    public string? ToastStatus => null;
    public ReminderSoundStatus SoundStatus => ReminderSoundStatus.NotConfigured;
    public string? SoundError => null;

    public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
    {
        return _owner.Dispatcher.InvokeAsync(() =>
        {
            var kind = notification.IsTodoLike ? "ToDo" : "予定";
            MessageBox.Show(
                _owner,
                $"{kind}の通知です。\n\n{notification.Title}\n{notification.DateDisplayText}",
                "リマインダー",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }).Task;
    }
}
