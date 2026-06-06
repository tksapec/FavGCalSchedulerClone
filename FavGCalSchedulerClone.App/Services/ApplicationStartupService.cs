using System.Windows;
using System.Windows.Threading;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.App.Services;

public sealed class ApplicationStartupService : IApplicationStartupService, IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly ReminderNotificationService _reminderService;
    private readonly WindowsToastInitializationService _toastInitializationService;
    private readonly DispatcherTimer _automaticSyncTimer;

    public ApplicationStartupService(
        MainViewModel viewModel,
        ReminderNotificationService reminderService,
        WindowsToastInitializationService toastInitializationService)
    {
        _viewModel = viewModel;
        _reminderService = reminderService;
        _toastInitializationService = toastInitializationService;
        _automaticSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _automaticSyncTimer.Tick += async (_, _) => await _viewModel.RunAutomaticSyncIfDueAsync();
    }

    public async Task InitializeAsync(Window owner, IReminderNotifier notifier)
    {
        try
        {
            await _viewModel.InitializeAsync();
            await _toastInitializationService.InitializeAsync();
            if (owner is FavGCalSchedulerClone.App.MainWindow mainWindow)
            {
                notifier = mainWindow.CreateReminderNotifier();
            }

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
