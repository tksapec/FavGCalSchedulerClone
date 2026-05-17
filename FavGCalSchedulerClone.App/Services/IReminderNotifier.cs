using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public interface IReminderNotifier
{
    Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default);
}
