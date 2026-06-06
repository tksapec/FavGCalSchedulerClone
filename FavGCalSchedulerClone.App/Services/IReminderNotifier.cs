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
    bool ToastVerified { get; }
    string? ToastStatus { get; }
}
