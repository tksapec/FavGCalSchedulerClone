using System.Windows;

namespace FavGCalSchedulerClone.App.Services;

public sealed class CustomPopupReminderNotifier : IReminderNotifier, IReminderNotifierMetadata
{
    private readonly Window _owner;
    private readonly Func<string, int, Task> _snoozeAsync;
    private readonly Func<Window, ReminderNotification, Func<int, Task>, CancellationToken, Task> _showPopupAsync;

    public CustomPopupReminderNotifier(Window owner, Func<string, int, Task> snoozeAsync)
        : this(owner, snoozeAsync, CustomReminderPopupWindow.ShowAsync)
    {
    }

    internal CustomPopupReminderNotifier(
        Func<string, int, Task> snoozeAsync,
        Func<Window, ReminderNotification, Func<int, Task>, CancellationToken, Task> showPopupAsync)
        : this(null!, snoozeAsync, showPopupAsync)
    {
    }

    internal CustomPopupReminderNotifier(
        Window owner,
        Func<string, int, Task> snoozeAsync,
        Func<Window, ReminderNotification, Func<int, Task>, CancellationToken, Task> showPopupAsync)
    {
        _owner = owner;
        _snoozeAsync = snoozeAsync;
        _showPopupAsync = showPopupAsync;
    }

    public string DeliveryMethodName => "CustomPopup";
    public bool UsedMessageBoxFallback => false;
    public MessageBoxNotificationRole MessageBoxRole => MessageBoxNotificationRole.None;
    public bool ToastVerified => false;
    public string? ToastStatus => null;
    public ReminderSoundStatus SoundStatus => ReminderSoundStatus.NotConfigured;
    public string? SoundError => null;

    public Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
    {
        return _showPopupAsync(
            _owner,
            notification,
            minutes => _snoozeAsync(notification.OccurrenceKey, minutes),
            cancellationToken);
    }
}
