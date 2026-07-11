using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FavGCalSchedulerClone.App.Collections;
using FavGCalSchedulerClone.App.Commands;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Win32;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    internal const int CalendarSnapshotCacheCapacity = 25;
    private const int NoPendingSyncInvocationKind = -1;
    private const string ScheduleTitleHistoryKey = "schedule:title-history";
    private const string ScheduleLocationHistoryKey = "schedule:location-history";
    private readonly CalendarRepository _repository;
    private readonly GoogleCalendarSyncService _syncService;
    private readonly BackupService _backupService;
    private readonly CalendarCsvService _csvService;
    private readonly FavGCalSchedulerImportService _favGCalImportService;
    private readonly IAppLogger? _logger;
    private readonly UndoService _undoService = new();
    private readonly BulkObservableCollection<CalendarDay> _calendarDays = [];
    private readonly BulkObservableCollection<CalendarDay> _visibleCalendarDays = [];
    private IReadOnlyList<CalendarEvent> _storedEvents = [];
    private IReadOnlyList<CalendarEvent> _visibleEvents = [];
    private IReadOnlyList<CalendarEvent> _dayDirectiveEvents = [];
    private int _monthLaneCapacity = CalendarSegmentLayoutService.MaxLanes;
    private AppSettings _settings = new();
    private DateTime _currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private CalendarDay? _selectedDay;
    private CalendarEvent? _selectedEvent;
    private string _status = "起動中...";
    private string _title = "";
    private string _description = "";
    private string _location = "";
    private DateTime _startDate = DateTime.Today;
    private DateTime _endDate = DateTime.Today;
    private string _startTime = "09:00";
    private string _endTime = "10:00";
    private bool _isAllDay = true;
    private int? _reminderMinutesBeforeStart;
    private IReadOnlyList<int> _appReminderMinutesBeforeStart = [];
    private IReadOnlyList<int> _googleEmailReminderMinutesBeforeStart = [];
    private bool _isAppReminderEnabled = true;
    private bool _isGoogleEmailReminderEnabled;
    private string _oauthClientJsonPath = "";
    private int _selectedTabIndex;
    private int _selectedTodoTabIndex;
    private DateTime? _pendingSelectedDate;
    private CalendarViewMode _currentViewMode = CalendarViewMode.Month;
    private string _editorCalendarId = GoogleCalendarDefaults.PrimaryCalendarId;
    private string? _editorColorId;
    private int _refreshGeneration;
    private CancellationTokenSource? _calendarRefreshCts;
    private CancellationTokenSource? _deferredCalendarRefreshCts;
    private readonly SemaphoreSlim _displayMonthPersistenceGate = new(1, 1);
    private CancellationTokenSource? _displayMonthPersistenceCts;
    private long _displayMonthPersistenceVersion;
    private CalendarCacheKey? _lastAppliedCalendarSnapshotKey;
    private CalendarRefreshSnapshot? _lastAppliedCalendarSnapshot;
    private DateTime? _navigationAnchorDate;
    private readonly object _calendarCacheLock = new();
    private readonly Dictionary<CalendarCacheKey, CalendarRefreshSnapshot> _calendarCache = [];
    private CalendarDataWindow? _calendarDataWindow;
    private long _calendarDataVersion;
    private IReadOnlyDictionary<string, EventDisplayColors> _eventColorPalette = TagService.DefaultEventColorPalette;
    private IReadOnlyList<string> _scheduleTitleHistory = [];
    private IReadOnlyList<string> _scheduleLocationHistory = [];
    private int _syncInProgress;
    private int _pendingSyncInvocationKind = NoPendingSyncInvocationKind;
    private int _calendarSelectionInProgress;
    private int _calendarSelectionRerunRequested;
    private bool _isSynchronizing;
    private LabelClipboardItem? _labelClipboard;
    private Func<SyncPreview, Task<bool>>? _confirmManualSyncPreviewAsync;
    private Func<Task>? _showAddScheduleAsync;
    private Func<Task>? _showAddTodoAsync;
    private Func<Task>? _backupAllCalendarsAsync;
    private Func<Task>? _restoreAllCalendarsAsync;
    private Func<Task>? _importFavGCalSchedulerAsync;
    private Func<Task>? _importCsvAsync;
    private Func<Task>? _exportCsvAsync;
    private Func<Task>? _showScheduleListAsync;
    private Func<Task>? _showSearchAsync;
    private Func<Task>? _showSyncDiagnosticsAsync;
    private Func<Task>? _showSettingsAsync;
    private Func<Task>? _showReminderHistoryAsync;
    private Func<Task>? _showAboutAsync;
    private Func<Task>? _showMonthJumpAsync;
    private string _syncStatusText = "同期: 未確認";
    private string _reminderStatusText = "通知監視: 未確認";
    private string _lastErrorStatusText = "";
    private TodoQuickFilter _todoQuickFilter = TodoQuickFilter.All;

    public MainViewModel(CalendarRepository repository, GoogleCalendarSyncService syncService)
        : this(repository, syncService, new BackupService(), new CalendarCsvService(), new FavGCalSchedulerImportService(repository), null)
    {
    }

    public MainViewModel(
        CalendarRepository repository,
        GoogleCalendarSyncService syncService,
        BackupService backupService,
        CalendarCsvService csvService,
        FavGCalSchedulerImportService favGCalImportService,
        IAppLogger? logger = null)
    {
        _repository = repository;
        _syncService = syncService;
        _backupService = backupService;
        _csvService = csvService;
        _favGCalImportService = favGCalImportService;
        _logger = logger;

        PreviousMonthCommand = new RelayCommand(() => NavigatePrimary(-1));
        NextMonthCommand = new RelayCommand(() => NavigatePrimary(1));
        PreviousYearCommand = new RelayCommand(() => NavigateSecondary(-1));
        NextYearCommand = new RelayCommand(() => NavigateSecondary(1));
        TodayCommand = CreateAsyncCommand(GoToTodayAsync);
        ShowMonthViewCommand = new RelayCommand(() => CurrentViewMode = CalendarViewMode.Month);
        ShowWeekViewCommand = new RelayCommand(() => CurrentViewMode = CalendarViewMode.Week);
        ShowDayViewCommand = new RelayCommand(() => CurrentViewMode = CalendarViewMode.Day);
        NewEventCommand = new RelayCommand(NewEvent);
        SaveEventCommand = CreateAsyncCommand(() => SaveEventWithRecurrenceAsync(null));
        DeleteEventCommand = CreateAsyncCommand(() => DeleteEventWithRecurrenceAsync(null), () => SelectedEvent is not null);
        MarkSelectedTodoDoneCommand = CreateAsyncCommand(MarkSelectedTodoDoneAsync, () => SelectedEvent?.IsTodoLike == true && !SelectedEvent.IsTodoDone);
        SyncCommand = CreateAsyncCommand(SynchronizeManuallyWithPreviewAsync);
        SyncDirtyCommand = CreateAsyncCommand(SynchronizeDirtyOnlyAsync);
        RefreshGoogleRemindersCommand = CreateAsyncCommand(async () => await RefreshGoogleReminderMetadataAsync());
        ReloadCalendarListCommand = CreateAsyncCommand(ReloadAvailableCalendarsAsync);
        BrowseOAuthClientCommand = CreateAsyncCommand(BrowseOAuthClientAsync);
        AuthorizeCommand = CreateAsyncCommand(AuthorizeAsync);
        ClearTokensCommand = CreateAsyncCommand(ClearTokensAsync);
        SaveTagsCommand = CreateAsyncCommand(SaveTagsAsync);
        AddScheduleCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_showAddScheduleAsync));
        AddTodoCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_showAddTodoAsync));
        BackupAllCalendarsCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_backupAllCalendarsAsync));
        RestoreAllCalendarsCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_restoreAllCalendarsAsync));
        ImportFavGCalSchedulerCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_importFavGCalSchedulerAsync));
        ImportCsvCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_importCsvAsync));
        ExportCsvCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_exportCsvAsync));
        ShowScheduleListCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_showScheduleListAsync));
        SearchCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_showSearchAsync));
        ShowSyncDiagnosticsCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_showSyncDiagnosticsAsync));
        ShowSettingsCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_showSettingsAsync));
        ShowReminderHistoryCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_showReminderHistoryAsync));
        ShowAboutCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_showAboutAsync));
        ShowMonthJumpCommand = CreateAsyncCommand(() => InvokeWindowCommandAsync(_showMonthJumpAsync));
        ShowAllTodosCommand = CreateAsyncCommand(() => SetTodoQuickFilterAsync(TodoQuickFilter.All));
        ShowTodayTodosCommand = CreateAsyncCommand(() => SetTodoQuickFilterAsync(TodoQuickFilter.Today));
        ShowOverdueTodosCommand = CreateAsyncCommand(() => SetTodoQuickFilterAsync(TodoQuickFilter.Overdue));
        ShowThisWeekTodosCommand = CreateAsyncCommand(() => SetTodoQuickFilterAsync(TodoQuickFilter.ThisWeek));
        ShowHighPriorityTodosCommand = CreateAsyncCommand(() => SetTodoQuickFilterAsync(TodoQuickFilter.HighPriority));
        IncreaseSelectedTodoProgressCommand = CreateAsyncCommand(() => UpdateSelectedTodoAsync(progressDelta: 10), () => SelectedEvent?.IsTodoLike == true && !SelectedEvent.IsTodoDone);
        SetSelectedTodoPriorityACommand = CreateAsyncCommand(() => UpdateSelectedTodoAsync(priority: "A"), () => SelectedEvent?.IsTodoLike == true && !SelectedEvent.IsTodoDone);
        SetSelectedTodoPriorityBCommand = CreateAsyncCommand(() => UpdateSelectedTodoAsync(priority: "B"), () => SelectedEvent?.IsTodoLike == true && !SelectedEvent.IsTodoDone);
        SetSelectedTodoPriorityCCommand = CreateAsyncCommand(() => UpdateSelectedTodoAsync(priority: "C"), () => SelectedEvent?.IsTodoLike == true && !SelectedEvent.IsTodoDone);
        UndoLastChangeCommand = CreateAsyncCommand(UndoLastChangeAsync, () => CanUndoLastChange);
    }

    private AsyncRelayCommand CreateAsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) =>
        new(execute, canExecute, HandleCommandExceptionAsync);

    private Task HandleCommandExceptionAsync(Exception exception)
    {
        Debug.WriteLine(exception);
        Status = $"操作に失敗しました: {exception.Message}";
        return Task.CompletedTask;
    }

    public ObservableCollection<CalendarDay> CalendarDays => _calendarDays;
    public ObservableCollection<CalendarDay> VisibleCalendarDays => _visibleCalendarDays;
    public ObservableCollection<CalendarEvent> SelectedDayEvents { get; } = [];
    public ObservableCollection<CalendarEvent> SevenDayEvents { get; } = [];
    public ObservableCollection<CalendarEvent> TodoEvents { get; } = [];
    public ObservableCollection<CalendarEvent> CompletedTodoEvents { get; } = [];
    public ObservableCollection<GoogleCalendarSelectionItem> AvailableCalendars { get; } = [];
    public ObservableCollection<CalendarTag> Tags { get; } = [];
    public ObservableCollection<string> CalendarNames { get; } = ["primary"];
    public IReadOnlyList<ReminderOption> ReminderOptions { get; } = ReminderOption.Defaults;

    public RelayCommand PreviousMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand PreviousYearCommand { get; }
    public RelayCommand NextYearCommand { get; }
    public AsyncRelayCommand TodayCommand { get; }
    public RelayCommand ShowMonthViewCommand { get; }
    public RelayCommand ShowWeekViewCommand { get; }
    public RelayCommand ShowDayViewCommand { get; }
    public RelayCommand NewEventCommand { get; }
    public AsyncRelayCommand SaveEventCommand { get; }
    public AsyncRelayCommand DeleteEventCommand { get; }
    public AsyncRelayCommand MarkSelectedTodoDoneCommand { get; }
    public AsyncRelayCommand SyncCommand { get; }
    public AsyncRelayCommand SyncDirtyCommand { get; }
    public AsyncRelayCommand RefreshGoogleRemindersCommand { get; }
    public AsyncRelayCommand ReloadCalendarListCommand { get; }
    public AsyncRelayCommand BrowseOAuthClientCommand { get; }
    public AsyncRelayCommand AuthorizeCommand { get; }
    public AsyncRelayCommand ClearTokensCommand { get; }
    public AsyncRelayCommand SaveTagsCommand { get; }
    public AsyncRelayCommand AddScheduleCommand { get; }
    public AsyncRelayCommand AddTodoCommand { get; }
    public AsyncRelayCommand BackupAllCalendarsCommand { get; }
    public AsyncRelayCommand RestoreAllCalendarsCommand { get; }
    public AsyncRelayCommand ImportFavGCalSchedulerCommand { get; }
    public AsyncRelayCommand ImportCsvCommand { get; }
    public AsyncRelayCommand ExportCsvCommand { get; }
    public AsyncRelayCommand ShowScheduleListCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand ShowSyncDiagnosticsCommand { get; }
    public AsyncRelayCommand ShowSettingsCommand { get; }
    public AsyncRelayCommand ShowReminderHistoryCommand { get; }
    public AsyncRelayCommand ShowAboutCommand { get; }
    public AsyncRelayCommand ShowMonthJumpCommand { get; }
    public AsyncRelayCommand ShowAllTodosCommand { get; }
    public AsyncRelayCommand ShowTodayTodosCommand { get; }
    public AsyncRelayCommand ShowOverdueTodosCommand { get; }
    public AsyncRelayCommand ShowThisWeekTodosCommand { get; }
    public AsyncRelayCommand ShowHighPriorityTodosCommand { get; }
    public AsyncRelayCommand IncreaseSelectedTodoProgressCommand { get; }
    public AsyncRelayCommand SetSelectedTodoPriorityACommand { get; }
    public AsyncRelayCommand SetSelectedTodoPriorityBCommand { get; }
    public AsyncRelayCommand SetSelectedTodoPriorityCCommand { get; }
    public AsyncRelayCommand UndoLastChangeCommand { get; }
    internal Func<DateTime, CancellationToken, Task>? BeforeLoadCalendarSnapshotAsync { get; set; }
    internal Action<DateTime, CancellationToken>? BeforeBuildCalendarSnapshot { get; set; }
    internal Action<DateTime>? BeforeSaveDisplayMonth { get; set; }
    internal Func<DateTime, Task>? BeforeSaveDisplayMonthAsync { get; set; }
    internal Action<CalendarRefreshSnapshot>? BeforeApplyCalendarSnapshot { get; set; }
    internal Action? BeforeRefreshTodos { get; set; }
    internal TimeSpan NavigationRefreshDelay { get; set; } = TimeSpan.FromMilliseconds(10);
    internal int CalendarCacheCount
    {
        get
        {
            lock (_calendarCacheLock)
            {
                return _calendarCache.Count;
            }
        }
    }

    public string MonthTitle => CurrentMonth.ToString("yyyy/MM", CultureInfo.InvariantCulture);
    public string JapaneseMonthTitle => CalendarStatusFormatter.FormatJapaneseMonthTitle(CurrentMonth);
    public string CurrentPeriodTitle => CurrentViewMode switch
    {
        CalendarViewMode.Month => JapaneseMonthTitle,
        CalendarViewMode.Week => CalendarStatusFormatter.FormatWeekTitle(SelectedDay?.Date ?? DateTime.Today, _settings.WeekStartsOnMonday),
        CalendarViewMode.Day => CalendarStatusFormatter.FormatDayTitle(SelectedDay?.Date ?? DateTime.Today),
        _ => JapaneseMonthTitle
    };
    public string CalendarStatusText => CalendarStatusFormatter.FormatCalendarStatus(SelectedDay?.Date ?? DateTime.Today);
    public bool CanUndoLastChange => _undoService.CanUndo;
    public string UndoStatusText => _undoService.StatusText;
    public bool IsMonthView => CurrentViewMode == CalendarViewMode.Month;
    public bool IsWeekView => CurrentViewMode == CalendarViewMode.Week;
    public bool IsDayView => CurrentViewMode == CalendarViewMode.Day;
    public string PreviousYearLabel => CurrentViewMode switch
    {
        CalendarViewMode.Month => "前年",
        CalendarViewMode.Week => "前月",
        CalendarViewMode.Day => "前月",
        _ => "前年"
    };
    public string PreviousMonthLabel => CurrentViewMode switch
    {
        CalendarViewMode.Month => "前月",
        CalendarViewMode.Week => "前週",
        CalendarViewMode.Day => "前日",
        _ => "前月"
    };
    public string NextMonthLabel => CurrentViewMode switch
    {
        CalendarViewMode.Month => "次月",
        CalendarViewMode.Week => "次週",
        CalendarViewMode.Day => "翌日",
        _ => "次月"
    };
    public string NextYearLabel => CurrentViewMode switch
    {
        CalendarViewMode.Month => "次年",
        CalendarViewMode.Week => "次月",
        CalendarViewMode.Day => "次月",
        _ => "次年"
    };

    public DateTime CurrentMonth
    {
        get => _currentMonth;
        set
        {
            if (SetProperty(ref _currentMonth, new DateTime(value.Year, value.Month, 1)))
            {
                OnPropertyChanged(nameof(MonthTitle));
                OnPropertyChanged(nameof(JapaneseMonthTitle));
                ScheduleDisplayMonthPersistence(_currentMonth);
                ShowImmediateCalendarShellForMonth(_currentMonth);
                ScheduleCalendarRefreshAfterNavigation();
            }
        }
    }

    public CalendarViewMode CurrentViewMode
    {
        get => _currentViewMode;
        set
        {
            if (SetProperty(ref _currentViewMode, value))
            {
                OnPropertyChanged(nameof(IsMonthView));
                OnPropertyChanged(nameof(IsWeekView));
                OnPropertyChanged(nameof(IsDayView));
                OnPropertyChanged(nameof(CurrentPeriodTitle));
                OnPropertyChanged(nameof(PreviousYearLabel));
                OnPropertyChanged(nameof(PreviousMonthLabel));
                OnPropertyChanged(nameof(NextMonthLabel));
                OnPropertyChanged(nameof(NextYearLabel));
                RefreshVisibleCalendarDays();
                if (value == CalendarViewMode.Month && CalendarDays.Count > 0)
                {
                    ApplySegmentLayout(CalendarDays, _monthLaneCapacity);
                    UpdateSegmentSelection();
                }
            }
        }
    }

    public CalendarDay? SelectedDay
    {
        get => _selectedDay;
        set
        {
            if (SetProperty(ref _selectedDay, value))
            {
                if (SelectedEvent is not null && (value is null || !DateRangeHelper.OccursOn(SelectedEvent, value.Date)))
                {
                    SelectedEvent = null;
                }

                UpdateSegmentSelection();
                RefreshSelectedDayEvents();
                RefreshSevenDayEvents();
                OnPropertyChanged(nameof(CurrentPeriodTitle));
                OnPropertyChanged(nameof(CalendarStatusText));
                if (value is not null)
                {
                    _navigationAnchorDate = value.Date;
                    StartDate = value.Date;
                    EndDate = value.Date;
                    Status = $"{value.Date:yyyy/MM/dd} を選択しました。";
                }
            }
        }
    }

    public CalendarEvent? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (SetProperty(ref _selectedEvent, value))
            {
                UpdateSegmentSelection();
                LoadEditor(value);
                DeleteEventCommand.RaiseCanExecuteChanged();
                MarkSelectedTodoDoneCommand.RaiseCanExecuteChanged();
                IncreaseSelectedTodoProgressCommand.RaiseCanExecuteChanged();
                SetSelectedTodoPriorityACommand.RaiseCanExecuteChanged();
                SetSelectedTodoPriorityBCommand.RaiseCanExecuteChanged();
                SetSelectedTodoPriorityCCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanCutSelectedEventLabel));
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsSynchronizing
    {
        get => _isSynchronizing;
        private set
        {
            if (SetProperty(ref _isSynchronizing, value))
            {
                SyncStatusText = value ? "同期: 実行中..." : SyncStatusText;
            }
        }
    }

    public string SyncStatusText
    {
        get => _syncStatusText;
        private set => SetProperty(ref _syncStatusText, value);
    }

    public string ReminderStatusText
    {
        get => _reminderStatusText;
        private set => SetProperty(ref _reminderStatusText, value);
    }

    public string LastErrorStatusText
    {
        get => _lastErrorStatusText;
        private set => SetProperty(ref _lastErrorStatusText, value);
    }

    public TodoQuickFilter TodoQuickFilter
    {
        get => _todoQuickFilter;
        private set
        {
            if (SetProperty(ref _todoQuickFilter, value))
            {
                OnPropertyChanged(nameof(TodoQuickFilterText));
            }
        }
    }

    public string TodoQuickFilterText => TodoQuickFilter switch
    {
        TodoQuickFilter.Today => "ToDo: 今日",
        TodoQuickFilter.Overdue => "ToDo: 期限切れ",
        TodoQuickFilter.ThisWeek => "ToDo: 今週",
        TodoQuickFilter.HighPriority => "ToDo: 高優先度",
        _ => "ToDo: すべて"
    };

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string Location
    {
        get => _location;
        set => SetProperty(ref _location, value);
    }

    public DateTime StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value.Date);
    }

    public DateTime EndDate
    {
        get => _endDate;
        set => SetProperty(ref _endDate, value.Date);
    }

    public string StartTime
    {
        get => _startTime;
        set => SetProperty(ref _startTime, value);
    }

    public string EndTime
    {
        get => _endTime;
        set => SetProperty(ref _endTime, value);
    }

    public bool IsAllDay
    {
        get => _isAllDay;
        set => SetProperty(ref _isAllDay, value);
    }

    public int? ReminderMinutesBeforeStart
    {
        get => _reminderMinutesBeforeStart;
        set => SetProperty(ref _reminderMinutesBeforeStart, value);
    }

    public IReadOnlyList<int> AppReminderMinutesBeforeStart
    {
        get => _appReminderMinutesBeforeStart;
        set => SetProperty(ref _appReminderMinutesBeforeStart, CalendarEvent.NormalizeReminderMinutes(value));
    }

    public IReadOnlyList<int> GoogleEmailReminderMinutesBeforeStart
    {
        get => _googleEmailReminderMinutesBeforeStart;
        set => SetProperty(ref _googleEmailReminderMinutesBeforeStart, CalendarEvent.NormalizeReminderMinutes(value));
    }

    public bool IsAppReminderEnabled
    {
        get => _isAppReminderEnabled;
        set => SetProperty(ref _isAppReminderEnabled, value);
    }

    public bool IsGoogleEmailReminderEnabled
    {
        get => _isGoogleEmailReminderEnabled;
        set => SetProperty(ref _isGoogleEmailReminderEnabled, value);
    }

    public string OAuthClientJsonPath
    {
        get => _oauthClientJsonPath;
        set => SetProperty(ref _oauthClientJsonPath, value);
    }

    public string EditorCalendarId
    {
        get => _editorCalendarId;
        set => SetProperty(ref _editorCalendarId, string.IsNullOrWhiteSpace(value) ? GoogleCalendarDefaults.PrimaryCalendarId : value);
    }

    public string? EditorColorId
    {
        get => _editorColorId;
        set => SetProperty(ref _editorColorId, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public IReadOnlyList<EventColorSelectionItem> EventColorOptions =>
        new[] { new EventColorSelectionItem(null, "標準（白）", TagService.DefaultDisplayColor, TagService.DefaultDisplayForegroundColor) }
            .Concat(Enumerable.Range(1, 11).Select(index =>
            {
                var id = index.ToString(CultureInfo.InvariantCulture);
                var colors = _eventColorPalette.TryGetValue(id, out var configured)
                    ? configured
                    : TagService.DefaultEventColorPalette[id];
                return new EventColorSelectionItem(id, $"色 {id}", colors.Background, colors.Foreground);
            }))
            .Select(ApplyEventColorSetting)
            .Where(item => item is not null)
            .Cast<EventColorSelectionItem>()
            .ToArray();

    public bool CanPasteEventLabel => _labelClipboard is not null;
    public bool CanCutSelectedEventLabel => SelectedEvent is not null && !SelectedEvent.IsRecurringSeriesItem;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, AppSettingsNormalizer.NormalizeTabIndex(value));
    }

    public int SelectedTodoTabIndex
    {
        get => _selectedTodoTabIndex;
        set => SetProperty(ref _selectedTodoTabIndex, Math.Clamp(value, 0, 1));
    }

    public int StartupTabIndex => _settings.StartupTabIndex;
    public CalendarViewMode StartupCalendarViewMode => _settings.StartupCalendarViewMode;
    public bool ConfirmBeforeDelete => _settings.ConfirmBeforeDelete;
    public bool CloseButtonExitsApplication => _settings.CloseButtonExitsApplication;
    public bool DefaultNewEventIsAllDay => _settings.DefaultNewEventIsAllDay;
    public bool HideMainWindowWhileEditingSchedule => _settings.HideMainWindowWhileEditingSchedule;
    public bool ReuseLastScheduleInput => _settings.ReuseLastScheduleInput;
    public int? DefaultScheduleReminderMinutes => _settings.DefaultScheduleReminderMinutes;
    public double CalendarLabelFontSize => _settings.CalendarLabelFontSizeIndex + 9;
    public double SideListFontSize => _settings.SideListFontSizeIndex + 10;
    public double WindowOpacity => _settings.WindowOpacity / 255.0;
    public IReadOnlyList<string> WeekdayHeaders => CalendarStatusFormatter.CreateWeekdayHeaders(_settings.WeekdayDisplayType, _settings.WeekStartsOnMonday);
    public IReadOnlyList<string> ScheduleTitleHistory => _scheduleTitleHistory;
    public IReadOnlyList<string> ScheduleLocationHistory => _scheduleLocationHistory;
    public bool EnableReminderSound => _settings.EnableReminderSound;
    public string? ReminderSoundFilePath => _settings.ReminderSoundFilePath;
    public int ReminderSoundVolume => _settings.ReminderSoundVolume;
    public string DefaultBackupFileName => $"FavGCalSchedulerClone-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip";

    public async Task InitializeAsync()
    {
        await _repository.InitializeAsync();
        _settings = AppSettingsNormalizer.Normalize(await _repository.LoadSettingsAsync());
        OAuthClientJsonPath = _settings.OAuthClientJsonPath ?? "";
        OnPropertyChanged(nameof(CalendarLabelFontSize));
        OnPropertyChanged(nameof(SideListFontSize));
        OnPropertyChanged(nameof(WindowOpacity));
        OnPropertyChanged(nameof(WeekdayHeaders));
        SelectedTabIndex = _settings.StartupTabIndex;
        SelectedTodoTabIndex = _settings.StartupTodoTabIndex;
        CurrentViewMode = _settings.StartupCalendarViewMode;
        await ReloadScheduleHistoryAsync();
        SelectedDay = null;
        await ReloadTagsAsync();
        _eventColorPalette = await _syncService.LoadCachedEventColorPaletteAsync();
        await ReloadAvailableCalendarsAsync();
        SetCurrentMonthWithoutRefreshing(_settings.DisplayMonth);
        await RefreshCalendarAsync();
        await RefreshOperationalStatusAsync(null);
        Status = "準備完了";
    }

    private async Task ReloadScheduleHistoryAsync()
    {
        _scheduleTitleHistory = DeserializeHistory(await _repository.LoadSettingValueAsync(ScheduleTitleHistoryKey));
        _scheduleLocationHistory = DeserializeHistory(await _repository.LoadSettingValueAsync(ScheduleLocationHistoryKey));
    }

    private enum SyncInvocationKind
    {
        LocalChange = 0,
        Automatic = 1,
        Manual = 2
    }

}

public sealed record EventColorSelectionItem(string? Id, string Label, string Background, string Foreground);

internal sealed record LabelClipboardItem(CalendarEvent Event, bool Cut);

internal sealed record CalendarRefreshRequest(
    int Generation,
    DateTime Month,
    DateTime? PendingSelectedDate,
    bool SaveDisplayMonth,
    bool RefreshTodos,
    CancellationToken CancellationToken);

internal sealed record CalendarCacheKey(DateTime Month, bool WeekStartsOnMonday, string VisibleCalendarIds, long DataVersion);

internal sealed record CalendarSnapshotBuildContext(
    bool WeekStartsOnMonday,
    IReadOnlySet<string> VisibleCalendarIds,
    IReadOnlyDictionary<string, string> CalendarNames,
    IReadOnlyDictionary<string, EventDisplayColors> EventColorPalette);

internal sealed record CalendarDataWindow(
    DateTime RangeStart,
    DateTime RangeEnd,
    bool WeekStartsOnMonday,
    long DataVersion,
    IReadOnlyList<CalendarEvent> StoredEvents,
    IReadOnlyList<CalendarEvent> ExpandedEvents,
    IReadOnlyDictionary<DateTime, IReadOnlyList<CalendarEvent>> EventsByDate);

internal sealed record CalendarRefreshSnapshot(
    DateTime Month,
    DateTime GridStart,
    DateTime GridEnd,
    IReadOnlyList<CalendarEvent> StoredEvents,
    IReadOnlyList<CalendarEvent> DayDirectiveEvents,
    IReadOnlyList<CalendarEvent> VisibleEvents);
