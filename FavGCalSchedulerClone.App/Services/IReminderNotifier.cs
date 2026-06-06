using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public interface IReminderNotifier
{
    Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default);
}

public interface IReminderNotifierMetadata
{
    string DeliveryMethodName { get; }
    bool UsedMessageBoxFallback { get; }
    MessageBoxNotificationRole MessageBoxRole { get; }
    bool ToastVerified { get; }
    string? ToastStatus { get; }
    ReminderSoundStatus SoundStatus { get; }
    string? SoundError { get; }
}

public enum MessageBoxNotificationRole
{
    None,
    Primary,
    AfterToast,
    Fallback
}

public enum ReminderSoundStatus
{
    NotConfigured,
    MissingFile,
    Played,
    Failed
}
