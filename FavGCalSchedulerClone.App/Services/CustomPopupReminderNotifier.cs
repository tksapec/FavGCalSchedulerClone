using System.Windows;

namespace FavGCalSchedulerClone.App.Services;

public sealed class CustomPopupReminderNotifier : IReminderNotifier, IReminderNotifierMetadata
{
    private readonly Window _owner;
    private readonly Func<Window, ReminderNotification, CancellationToken, Task> _showPopupAsync;

    public CustomPopupReminderNotifier(Window owner)
        : this(owner, CustomReminderPopupWindow.ShowAsync)
    {
    }

    internal CustomPopupReminderNotifier(
        Func<Window, ReminderNotification, CancellationToken, Task> showPopupAsync)
        : this(null!, showPopupAsync)
    {
    }

    internal CustomPopupReminderNotifier(
        Window owner,
        Func<Window, ReminderNotification, CancellationToken, Task> showPopupAsync)
    {
        _owner = owner;
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
            cancellationToken);
    }
}
