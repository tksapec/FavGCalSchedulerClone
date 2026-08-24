using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly ReminderNotificationService? _reminderService;
    private int _databaseMaintenanceInProgress;

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
}
