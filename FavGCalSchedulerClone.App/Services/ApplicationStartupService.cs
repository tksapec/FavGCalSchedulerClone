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
    private bool _disposed;

    public ApplicationStartupService(MainViewModel viewModel, ReminderNotificationService reminderService, IAppLogger? logger = null)
    {
        _viewModel = viewModel;
        _reminderService = reminderService;
        _logger = logger;
        _automaticSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _automaticSyncTimer.Tick += AutomaticSyncTimer_Tick;
    }

    public async Task InitializeAsync(Window? owner, Func<IReminderNotifier> notifierFactory)
    {
        if (_disposed)
        {
            return;
        }

        var ownerWasEnabled = owner?.IsEnabled;
        if (owner is not null)
        {
            owner.IsEnabled = false;
        }
        try
        {
            try
            {
                await _viewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Main view model initialization failed.");
                if (_disposed)
                {
                    return;
                }

                MessageBox.Show(owner, ex.Message, "初期化エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }

            if (_disposed)
            {
                return;
            }

            try
            {
                _reminderService.SetNotifier(notifierFactory());
                await _reminderService.StartAsync();
                if (_disposed)
                {
                    _reminderService.Stop();
                    return;
                }

                _viewModel.Status = "通知監視を開始しました";
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                _logger?.LogError(ex, "Reminder monitoring startup failed.");
                if (_disposed)
                {
                    return;
                }

                MessageBox.Show(owner, $"通知監視を開始できませんでした。\n{ex.Message}", "通知エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            if (_disposed)
            {
                return;
            }

            try
            {
                _automaticSyncTimer.Start();
                if (_disposed)
                {
                    _automaticSyncTimer.Stop();
                    return;
                }

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
        finally
        {
            if (owner is not null
                && ownerWasEnabled is not null
                && !_disposed
                && !_viewModel.IsDatabaseMaintenanceInProgress
                && !_viewModel.IsDatabaseRestartRequired)
            {
                owner.IsEnabled = ownerWasEnabled.Value;
            }
        }
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _automaticSyncTimer.Stop();
        _reminderService.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _automaticSyncTimer.Stop();
        _automaticSyncTimer.Tick -= AutomaticSyncTimer_Tick;
        // ReminderNotificationService is a DI-managed singleton and is disposed by the
        // service provider. Do not dispose it here as well; MainWindow may also have
        // already stopped it during an explicit tray exit.
    }

    private async void AutomaticSyncTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.RunAutomaticSyncIfDueAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _logger?.LogError(ex, "Automatic Google calendar sync failed.");
        }
    }
}
