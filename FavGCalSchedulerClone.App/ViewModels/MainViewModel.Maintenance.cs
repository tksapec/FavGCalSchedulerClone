using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly ReminderNotificationService? _reminderService;
    private int _databaseMaintenanceInProgress;

    public bool IsDatabaseMaintenanceInProgress =>
        Volatile.Read(ref _databaseMaintenanceInProgress) != 0;

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

        OnPropertyChanged(nameof(IsDatabaseMaintenanceInProgress));
        return true;
    }

    private void EndDatabaseMaintenanceState()
    {
        if (Interlocked.Exchange(ref _databaseMaintenanceInProgress, 0) != 0)
        {
            OnPropertyChanged(nameof(IsDatabaseMaintenanceInProgress));
        }
    }
}
