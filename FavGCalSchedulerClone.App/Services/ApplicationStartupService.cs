using System.Windows;
using System.Windows.Threading;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.App.Services;

public sealed class ApplicationStartupService : IApplicationStartupService, IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly ReminderNotificationService _reminderService;
    private readonly DispatcherTimer _automaticSyncTimer;

    public ApplicationStartupService(MainViewModel viewModel, ReminderNotificationService reminderService)
    {
        _viewModel = viewModel;
        _reminderService = reminderService;
        _automaticSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _automaticSyncTimer.Tick += async (_, _) => await _viewModel.RunAutomaticSyncIfDueAsync();
    }

    public async Task InitializeAsync(Window owner, IReminderNotifier notifier)
    {
        try
        {
            await _viewModel.InitializeAsync();
            _reminderService.SetNotifier(notifier);
            await _reminderService.StartAsync();
            _automaticSyncTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Stop()
    {
        _automaticSyncTimer.Stop();
        _reminderService.Stop();
    }

    public void Dispose()
    {
        Stop();
        _reminderService.Dispose();
    }
}
