using System.ComponentModel;
using System.Windows;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.App;

public partial class MainWindow
{
    private bool? _mainWindowEnabledBeforeDatabaseMaintenance;
    private bool _databaseMaintenanceObserverAttached;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_databaseMaintenanceObserverAttached)
        {
            return;
        }

        _databaseMaintenanceObserverAttached = true;
        _viewModel.PropertyChanged += MainViewModel_DatabaseMaintenancePropertyChanged;
        ApplyDatabaseMaintenanceInteractionState();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_databaseMaintenanceObserverAttached)
        {
            _viewModel.PropertyChanged -= MainViewModel_DatabaseMaintenancePropertyChanged;
            _databaseMaintenanceObserverAttached = false;
        }

        base.OnClosed(e);
    }

    private void MainViewModel_DatabaseMaintenancePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MainViewModel.IsDatabaseMaintenanceInProgress), StringComparison.Ordinal)
            && !string.Equals(e.PropertyName, nameof(MainViewModel.IsDatabaseRestartRequired), StringComparison.Ordinal))
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ApplyDatabaseMaintenanceInteractionState);
            return;
        }

        ApplyDatabaseMaintenanceInteractionState();
    }

    private void ApplyDatabaseMaintenanceInteractionState()
    {
        if (_viewModel.IsDatabaseMaintenanceInProgress || _viewModel.IsDatabaseRestartRequired)
        {
            if (_mainWindowEnabledBeforeDatabaseMaintenance is null)
            {
                var wasEnabled = IsEnabled;
                _mainWindowEnabledBeforeDatabaseMaintenance = wasEnabled;
                IsEnabled = false;
            }

            return;
        }

        if (_mainWindowEnabledBeforeDatabaseMaintenance is { } wasEnabled)
        {
            _mainWindowEnabledBeforeDatabaseMaintenance = null;
            IsEnabled = wasEnabled;
        }
    }
}
