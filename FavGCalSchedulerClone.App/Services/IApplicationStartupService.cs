using System.Windows;

namespace FavGCalSchedulerClone.App.Services;

public interface IApplicationStartupService
{
    Task InitializeAsync(Window? owner, Func<IReminderNotifier> notifierFactory);
    void Stop();
}
