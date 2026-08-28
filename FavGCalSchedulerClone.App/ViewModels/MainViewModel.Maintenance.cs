using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly ReminderNotificationService? _reminderService;
    private int _databaseMaintenanceInProgress;
    private int _databaseRestartRequired;

    public bool IsDatabaseMaintenanceInProgress =>
        Volatile.Read(ref _databaseMaintenanceInProgress) != 0
        && Volatile.Read(ref _databaseRestartRequired) == 0;

    public bool IsDatabaseRestartRequired =>
        Volatile.Read(ref _databaseRestartRequired) != 0;

    public MainViewModel(
        CalendarRepository repository,
        GoogleCalendarSyncService syncService,
        BackupService backupService,
        CalendarCsvService csvService,
        FavGCalSchedulerImportService favGCalImportService,
        IAppLogger? logger,
        ReminderNotificationService reminderService)
        : this(repository, syncService, backupService, csvService, favGCalImportService, logger)
    {
        _reminderService = reminderService;
    }

    private bool TryBeginDatabaseMaintenanceState()
    {
        if (Interlocked.CompareExchange(ref _databaseMaintenanceInProgress, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            OnPropertyChanged(nameof(IsDatabaseMaintenanceInProgress));
            return true;
        }
        catch
        {
            Interlocked.Exchange(ref _databaseMaintenanceInProgress, 0);
            try
            {
                OnPropertyChanged(nameof(IsDatabaseMaintenanceInProgress));
            }
            catch (Exception rollbackNotificationException)
            {
                _logger?.LogError(rollbackNotificationException, "Database maintenance rollback notification failed.");
            }
            throw;
        }
    }

    private void EndDatabaseMaintenanceState()
    {
        // After a restored database has only been partially reloaded into the ViewModel,
        // keep the raw maintenance gate latched until process restart. Public UI state is
        // represented separately by IsDatabaseRestartRequired so "busy" and "restart"
        // remain distinguishable while all background DB operations stay blocked.
        if (IsDatabaseRestartRequired)
        {
            return;
        }

        if (Interlocked.Exchange(ref _databaseMaintenanceInProgress, 0) == 0)
        {
            return;
        }

        try
        {
            OnPropertyChanged(nameof(IsDatabaseMaintenanceInProgress));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Database maintenance completion notification failed.");
        }
    }

    private void MarkDatabaseRestartRequired()
    {
        if (Interlocked.Exchange(ref _databaseRestartRequired, 1) != 0)
        {
            return;
        }

        try
        {
            OnPropertyChanged(nameof(IsDatabaseRestartRequired));
            OnPropertyChanged(nameof(IsDatabaseMaintenanceInProgress));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Database restart-required notification failed.");
        }
    }

    private string GetDatabaseOperationBlockedMessage() =>
        IsDatabaseRestartRequired
            ? "バックアップDBの復元後に画面状態を完全に再読み込みできなかったため、アプリを再起動するまでデータ操作を開始できません。"
            : "データベースのリストア中はデータ操作を開始できません。";
}
