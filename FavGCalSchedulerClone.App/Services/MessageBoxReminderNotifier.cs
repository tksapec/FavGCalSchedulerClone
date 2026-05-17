using System.Windows;

namespace FavGCalSchedulerClone.App.Services;

public sealed class MessageBoxReminderNotifier : IReminderNotifier
{
    private readonly Window _owner;

    public MessageBoxReminderNotifier(Window owner)
    {
        _owner = owner;
    }

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
