using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using FavGCalSchedulerClone.App;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.App.Services;

public sealed class ApplicationStartupService : IApplicationStartupService, IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly ReminderNotificationService _reminderService;
    private readonly DispatcherTimer _automaticSyncTimer;
    private readonly IAppLogger? _logger;

    public ApplicationStartupService(MainViewModel viewModel, ReminderNotificationService reminderService, IAppLogger? logger = null)
    {
        _viewModel = viewModel;
        _reminderService = reminderService;
        _logger = logger;
        _automaticSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _automaticSyncTimer.Tick += async (_, _) => await _viewModel.RunAutomaticSyncIfDueAsync();
    }

    public async Task InitializeAsync(Window owner, Func<IReminderNotifier> notifierFactory)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Main view model initialization failed.");
            MessageBox.Show(owner, ex.Message, "初期化エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        try
        {
            _reminderService.SetNotifier(notifierFactory());
            await _reminderService.StartAsync();
            _viewModel.Status = "通知監視を開始しました";
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _logger?.LogError(ex, "Reminder monitoring startup failed.");
            MessageBox.Show(owner, $"通知監視を開始できませんでした。\n{ex.Message}", "通知エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        try
        {
            _automaticSyncTimer.Start();
            if (owner is MainWindow mainWindow)
            {
                mainWindow.StartOperationalStatusRefresh();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _logger?.LogError(ex, "Post-startup timer initialization failed.");
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
