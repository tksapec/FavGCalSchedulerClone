using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FavGCalSchedulerClone.App.Commands;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Win32;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string ScheduleTitleHistoryKey = "schedule:title-history";
    private const string ScheduleLocationHistoryKey = "schedule:location-history";
    private readonly CalendarRepository _repository;
    private readonly GoogleCalendarSyncService _syncService;
    private readonly BackupService _backupService;
    private readonly CalendarCsvService _csvService;
    private readonly FavGCalSchedulerImportService _favGCalImportService;
    private IReadOnlyList<CalendarEvent> _storedEvents = [];
    private IReadOnlyList<CalendarEvent> _visibleEvents = [];
    private IReadOnlyList<CalendarEvent> _dayDirectiveEvents = [];
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
    private DateTime? _navigationAnchorDate;
    private readonly Dictionary<CalendarCacheKey, CalendarRefreshSnapshot> _calendarCache = [];
    private IReadOnlyDictionary<string, EventDisplayColors> _eventColorPalette = TagService.DefaultEventColorPalette;
    private IReadOnlyList<string> _scheduleTitleHistory = [];
    private IReadOnlyList<string> _scheduleLocationHistory = [];
    private int _syncInProgress;
    private int _syncRerunRequested;
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
        : this(repository, syncService, new BackupService(), new CalendarCsvService(), new FavGCalSchedulerImportService(repository))
    {
    }

    public MainViewModel(
        CalendarRepository repository,
        GoogleCalendarSyncService syncService,
        BackupService backupService,
        CalendarCsvService csvService,
        FavGCalSchedulerImportService favGCalImportService)
    {
        _repository = repository;
        _syncService = syncService;
        _backupService = backupService;
        _csvService = csvService;
        _favGCalImportService = favGCalImportService;

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
    }

    private AsyncRelayCommand CreateAsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) =>
        new(execute, canExecute, HandleCommandExceptionAsync);

    private Task HandleCommandExceptionAsync(Exception exception)
    {
        Debug.WriteLine(exception);
        Status = $"操作に失敗しました: {exception.Message}";
        return Task.CompletedTask;
    }

    public ObservableCollection<CalendarDay> CalendarDays { get; } = [];
    public ObservableCollection<CalendarDay> VisibleCalendarDays { get; } = [];
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
    internal Func<DateTime, CancellationToken, Task>? BeforeLoadCalendarSnapshotAsync { get; set; }
    internal Action<DateTime, CancellationToken>? BeforeBuildCalendarSnapshot { get; set; }
    internal Action<DateTime>? BeforeSaveDisplayMonth { get; set; }
    internal Action? BeforeRefreshTodos { get; set; }
    internal TimeSpan NavigationRefreshDelay { get; set; } = TimeSpan.FromMilliseconds(80);
    internal int CalendarCacheCount => _calendarCache.Count;
    internal bool IsCalendarMonthCached(DateTime month) => TryGetCalendarCache(month) is not null;

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

    private EventColorSelectionItem? ApplyEventColorSetting(EventColorSelectionItem item)
    {
        if (item.Id is null)
        {
            return item;
        }

        var setting = _settings.EventColorSettings.FirstOrDefault(setting => setting.ColorId == item.Id);
        if (setting?.IsEnabled == false)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(setting?.Label)
            ? item
            : item with { Label = setting.Label! };
    }

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

    public async Task RefreshOperationalStatusAsync(ReminderMonitoringSnapshot? reminderDiagnostics)
    {
        var diagnostics = await _syncService.LoadDiagnosticsAsync(_settings);
        SyncStatusText = diagnostics.LastResult is null
            ? $"同期: 未同期 {diagnostics.DirtyCount} 件 / 最終同期なし"
            : $"同期: 未同期 {diagnostics.DirtyCount} 件 / 最終 {diagnostics.LastResult.FinishedAt:MM/dd HH:mm}";
        if (reminderDiagnostics is not null)
        {
            ReminderStatusText = FormatReminderStatus(reminderDiagnostics);
        }

        LastErrorStatusText = BuildLastErrorStatus(diagnostics, reminderDiagnostics);
    }

    public void UpdateReminderOperationalStatus(ReminderMonitoringSnapshot reminderDiagnostics)
    {
        ReminderStatusText = FormatReminderStatus(reminderDiagnostics);
        LastErrorStatusText = BuildLastErrorStatus(null, reminderDiagnostics);
    }

    public void NewEvent()
    {
        BeginNewEvent(SelectedDay?.Date ?? DateTime.Today);
    }

    public async Task GoToTodayAsync()
    {
        _pendingSelectedDate = DateTime.Today;
        _navigationAnchorDate = DateTime.Today;
        SetCurrentMonthWithoutRefreshing(DateTime.Today);
        ShowImmediateCalendarShellForMonth(CurrentMonth);
        await RefreshCalendarAsync(invalidateCache: false, refreshTodos: false);
        Status = "今日を表示しました。";
    }

    public async Task SelectReminderEventAsync(string eventId, DateTimeOffset occurrenceStart)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return;
        }

        await NavigateToDateAsync(occurrenceStart.Date);
        SelectedEvent = _visibleEvents.FirstOrDefault(item =>
                string.Equals(item.Id, eventId, StringComparison.Ordinal)
                && item.Start.Date == occurrenceStart.Date)
            ?? _visibleEvents.FirstOrDefault(item => string.Equals(item.Id, eventId, StringComparison.Ordinal));
    }

    public async Task NavigateToDateAsync(DateTime targetDate)
    {
        _pendingSelectedDate = targetDate.Date;
        _navigationAnchorDate = targetDate.Date;
        SetCurrentMonthWithoutRefreshing(targetDate.Date);
        ShowImmediateCalendarShellForMonth(CurrentMonth);
        await RefreshCalendarAsync(invalidateCache: false, refreshTodos: false);
    }

    public void SelectEvent(CalendarEvent calendarEvent, bool selectEventDay = true)
    {
        if (selectEventDay)
        {
            var day = CalendarDays.FirstOrDefault(d => DateRangeHelper.OccursOn(calendarEvent, d.Date))
                ?? FindOrCreateCalendarDay(calendarEvent.Start.Date);
            SelectedDay = day;
        }

        SelectedEvent = calendarEvent;
    }

    public void SelectEventSegment(CalendarEventSegment segment)
    {
        if (segment.Event is null)
        {
            return;
        }

        SelectedDay = CalendarDays.FirstOrDefault(day => day.Date == segment.Date)
            ?? FindOrCreateCalendarDay(segment.Date);
        SelectedEvent = segment.Event;
    }

    public async Task<bool> MoveEventAsync(
        CalendarEvent calendarEvent,
        DateTime sourceSegmentDate,
        DateTime targetDate,
        RecurrenceEditScope? recurrenceScope = null)
    {
        var dayShift = (targetDate.Date - sourceSegmentDate.Date).Days;
        if (dayShift == 0 || calendarEvent.IsRecurringSeriesItem && recurrenceScope is null)
        {
            return false;
        }

        SelectedEvent = calendarEvent;
        var candidate = CloneEventForEditing(calendarEvent);
        candidate.Start = candidate.Start.AddDays(dayShift);
        candidate.End = candidate.End.AddDays(dayShift);
        candidate.IsDirty = true;

        if (!calendarEvent.IsRecurringSeriesItem)
        {
            await SaveEventWithCalendarMoveAsync(candidate, calendarEvent);
            SelectedEvent = candidate;
        }
        else
        {
            switch (recurrenceScope!.Value)
            {
                case RecurrenceEditScope.ThisOccurrence:
                    await SaveSingleOccurrenceAsync(candidate);
                    break;
                case RecurrenceEditScope.ThisAndFollowing:
                    await SaveThisAndFollowingAsync(candidate);
                    break;
                case RecurrenceEditScope.AllEvents:
                    await SaveEntireSeriesAsync(candidate);
                    break;
            }
        }

        _pendingSelectedDate = targetDate.Date;
        var movedEvent = SelectedEvent;
        await RefreshCalendarAsync();
        SelectedEvent = FindMovedVisibleEvent(movedEvent, candidate, targetDate) ?? movedEvent ?? candidate;
        UpdateSegmentSelection();
        Status = calendarEvent.IsTodoLike ? "ToDoを移動しました。" : "予定を移動しました。";
        await SyncAfterLocalChangeAsync();
        return true;
    }

    public void CopySelectedEventLabel()
    {
        if (SelectedEvent is null)
        {
            return;
        }

        _labelClipboard = new LabelClipboardItem(CloneEventForEditing(SelectedEvent), Cut: false);
        OnPropertyChanged(nameof(CanPasteEventLabel));
    }

    public void CutSelectedEventLabel()
    {
        if (!CanCutSelectedEventLabel || SelectedEvent is null)
        {
            return;
        }

        _labelClipboard = new LabelClipboardItem(CloneEventForEditing(SelectedEvent), Cut: true);
        OnPropertyChanged(nameof(CanPasteEventLabel));
    }

    public async Task<bool> PasteEventLabelAsync(DateTime targetDate)
    {
        if (_labelClipboard is not { } clipboard)
        {
            return false;
        }

        var pasted = CloneEventAsNewLocalEvent(clipboard.Event);
        var dayShift = (targetDate.Date - clipboard.Event.Start.Date).Days;
        pasted.Start = pasted.Start.AddDays(dayShift);
        pasted.End = pasted.End.AddDays(dayShift);
        await _repository.SaveEventAsync(pasted);

        if (clipboard.Cut)
        {
            var source = clipboard.Event;
            source.IsDeleted = true;
            source.IsDirty = true;
            await _repository.SaveEventAsync(source);
            _labelClipboard = null;
            OnPropertyChanged(nameof(CanPasteEventLabel));
        }

        _pendingSelectedDate = targetDate.Date;
        SelectedEvent = pasted;
        await RefreshCalendarAsync();
        await SyncAfterLocalChangeAsync();
        return true;
    }

    public void BeginNewEvent(DateTime date)
    {
        SelectedEvent = null;
        Title = _settings.ReuseLastScheduleInput ? _scheduleTitleHistory.FirstOrDefault() ?? "" : "";
        Description = "";
        Location = _settings.ReuseLastScheduleInput ? _scheduleLocationHistory.FirstOrDefault() ?? "" : "";
        StartDate = date.Date;
        EndDate = date.Date;
        StartTime = "09:00";
        EndTime = "10:00";
        IsAllDay = _settings.DefaultNewEventIsAllDay;
        ReminderMinutesBeforeStart = _settings.DefaultScheduleReminderMinutes;
        EditorColorId = null;
        Status = "新しいスケジュールを入力してください。";
    }

    public async Task SaveCurrentEventAsync(RecurrenceEditScope? recurrenceScope = null)
    {
        await SaveEventWithRecurrenceAsync(recurrenceScope);
    }

    public async Task SaveTodoAsync(DateTime dueDate, string priority, int progress, string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            Status = "件名を入力してください。";
            return;
        }

        var todoEvent = new CalendarEvent
        {
            Title = title.Trim(),
            Description = TagService.UpdateTodoMarker(description, priority, progress),
            CalendarId = ResolveEditorCalendarId(),
            Start = new DateTimeOffset(dueDate.Date),
            End = new DateTimeOffset(dueDate.Date.AddDays(1)),
            IsAllDay = true,
            IsDirty = true,
            IsDeleted = false,
            IsTodoLike = true,
            ReminderMinutesBeforeStart = null,
            ColorId = EditorColorId
        };

        await _repository.SaveEventAsync(todoEvent);
        await RefreshCalendarAsync();
        Status = "ToDoを保存しました。同期するとGoogleカレンダーへ反映されます。";
        await SyncAfterLocalChangeAsync();
    }

    public async Task SaveTodoAsync(CalendarEvent editingTodo, DateTime dueDate, string priority, int progress, string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            Status = "件名を入力してください。";
            return;
        }

        var originalTodo = CloneEventForEditing(editingTodo);
        editingTodo.Title = title.Trim();
        editingTodo.Description = TagService.UpdateTodoMarker(description, priority, progress);
        editingTodo.CalendarId = ResolveEditorCalendarId();
        editingTodo.Start = new DateTimeOffset(dueDate.Date);
        editingTodo.End = new DateTimeOffset(dueDate.Date.AddDays(1));
        editingTodo.IsAllDay = true;
        editingTodo.IsDirty = true;
        editingTodo.IsDeleted = false;
        editingTodo.IsTodoLike = true;
        editingTodo.ColorId = EditorColorId;

        await SaveEventWithCalendarMoveAsync(editingTodo, originalTodo);
        await RefreshCalendarAsync();
        SelectedEvent = _visibleEvents.FirstOrDefault(item => item.Id == editingTodo.Id) ?? editingTodo;
        Status = "ToDoを保存しました。同期するとGoogleカレンダーへ反映されます。";
        await SyncAfterLocalChangeAsync();
    }

    public async Task SaveTodoAsync(string eventId, DateTime dueDate, string priority, int progress, string title, string? description)
    {
        var editingTodo = _storedEvents.FirstOrDefault(item => item.Id == eventId)
            ?? _visibleEvents.FirstOrDefault(item => item.Id == eventId);
        if (editingTodo is null)
        {
            await SaveTodoAsync(dueDate, priority, progress, title, description);
            return;
        }

        await SaveTodoAsync(editingTodo, dueDate, priority, progress, title, description);
    }

    public async Task MarkSelectedTodoDoneAsync()
    {
        if (SelectedEvent is null || !SelectedEvent.IsTodoLike)
        {
            return;
        }

        var priority = SelectedEvent.TodoPriority;
        SelectedEvent.Description = TagService.UpdateTodoMarker(SelectedEvent.Description, priority, 100);
        SelectedEvent.IsDirty = true;
        await _repository.SaveEventAsync(SelectedEvent);
        await RefreshCalendarAsync();
        MarkSelectedTodoDoneCommand.RaiseCanExecuteChanged();
        Status = "ToDoを処理済みにしました。同期するとGoogleカレンダーへ反映されます。";
        await SyncAfterLocalChangeAsync();
    }

    public async Task MarkTodoDoneAsync(CalendarEvent todoEvent)
    {
        if (!todoEvent.IsTodoLike)
        {
            return;
        }

        var priority = todoEvent.TodoPriority;
        todoEvent.Description = TagService.UpdateTodoMarker(todoEvent.Description, priority, 100);
        todoEvent.IsDirty = true;
        await _repository.SaveEventAsync(todoEvent);
        await RefreshCalendarAsync();
        MarkSelectedTodoDoneCommand.RaiseCanExecuteChanged();
        Status = "ToDoを処理済みにしました。同期するとGoogleカレンダーへ反映されます。";
        await SyncAfterLocalChangeAsync();
    }

    public async Task SetTodoQuickFilterAsync(TodoQuickFilter filter)
    {
        TodoQuickFilter = filter;
        await RefreshTodosAsync();
    }

    public async Task UpdateSelectedTodoAsync(string? priority = null, int? progressDelta = null)
    {
        if (SelectedEvent is not { IsTodoLike: true } todoEvent || todoEvent.IsTodoDone)
        {
            return;
        }

        var metadata = todoEvent.TodoMetadata;
        var nextPriority = string.IsNullOrWhiteSpace(priority) ? metadata?.Priority ?? "A" : priority;
        var nextProgress = Math.Clamp((metadata?.Progress ?? 0) + (progressDelta ?? 0), 0, 100);
        todoEvent.Description = TagService.UpdateTodoMarker(todoEvent.Description, nextPriority, nextProgress);
        todoEvent.IsDirty = true;
        await _repository.SaveEventAsync(todoEvent);
        await RefreshCalendarAsync();
        SelectedEvent = _visibleEvents.FirstOrDefault(item => item.Id == todoEvent.Id) ?? todoEvent;
        Status = $"ToDoを更新しました: 優先度 {nextPriority} / 進捗 {nextProgress}%";
        await SyncAfterLocalChangeAsync();
    }

    public async Task<CalendarEvent> CreateTwoMinuteReminderTestEventAsync()
    {
        var start = DateTimeOffset.Now.AddMinutes(2);
        var testEvent = new CalendarEvent
        {
            CalendarId = ResolveEditorCalendarId(),
            Title = "通知確認テスト",
            Description = "2分後の通知確認用に作成されました。",
            Start = start,
            End = start.AddMinutes(30),
            IsAllDay = false,
            ReminderMinutesBeforeStart = 0,
            IsDirty = true
        };
        await _repository.SaveEventAsync(testEvent);
        await RefreshCalendarAsync();
        SelectedEvent = _visibleEvents.FirstOrDefault(item => item.Id == testEvent.Id) ?? testEvent;
        Status = "2分後の通知確認テスト予定を作成しました。";
        await RefreshOperationalStatusAsync(null);
        return testEvent;
    }

    public async Task<IReadOnlyList<CalendarEvent>> LoadYearEventsAsync(DateTime yearInView)
    {
        var start = new DateTime(yearInView.Year, 1, 1);
        var end = start.AddYears(1);
        var events = await _repository.LoadEventsAsync(new DateTimeOffset(start), new DateTimeOffset(end));
        var expanded = RecurrenceExpansionService.ExpandForRange(events, new DateTimeOffset(start), new DateTimeOffset(end));
        ApplyDisplayColors(expanded);
        return expanded.Where(IsVisible).OrderBy(e => e.Start).ThenBy(e => e.Title).ToArray();
    }

    public async Task<IReadOnlyList<CalendarEvent>> SearchYearEventsAsync(DateTime yearInView, string query)
    {
        return await SearchEventsAsync(new EventListFilter(query, EventKindFilter.All, EventSearchRange.Year, yearInView));
    }

    public async Task<IReadOnlyList<CalendarEvent>> SearchEventsAsync(EventListFilter filter)
    {
        var (start, end) = ResolveSearchRange(filter);
        var events = await _repository.LoadEventsAsync(new DateTimeOffset(start), new DateTimeOffset(end));
        var expanded = RecurrenceExpansionService.ExpandForRange(events, new DateTimeOffset(start), new DateTimeOffset(end));
        ApplyDisplayColors(expanded);
        var visible = expanded.Where(IsVisible);
        if (!string.IsNullOrWhiteSpace(filter.CalendarId))
        {
            visible = visible.Where(e => string.Equals(e.CalendarId, filter.CalendarId, StringComparison.Ordinal));
        }

        visible = filter.KindFilter switch
        {
            EventKindFilter.Schedule => visible.Where(e => !e.IsTodoLike),
            EventKindFilter.Todo => visible.Where(e => e.IsTodoLike),
            _ => visible
        };

        var query = filter.Query.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return visible.OrderBy(e => e.Start).ThenBy(e => e.Title).ToArray();
        }

        return visible
            .Where(e => e.SearchText.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(e => e.Start)
            .ThenBy(e => e.Title)
            .ToArray();
    }

    private static (DateTime Start, DateTime End) ResolveSearchRange(EventListFilter filter)
    {
        var date = filter.ReferenceDate.Date;
        if (filter.Range == EventSearchRange.Custom)
        {
            var start = (filter.StartDate ?? date).Date;
            var end = (filter.EndDate ?? start).Date;
            if (end < start)
            {
                (start, end) = (end, start);
            }

            return (start, end.AddDays(1));
        }

        return filter.Range switch
        {
            EventSearchRange.Day => (date, date.AddDays(1)),
            EventSearchRange.Month => (new DateTime(date.Year, date.Month, 1), new DateTime(date.Year, date.Month, 1).AddMonths(1)),
            EventSearchRange.All => (new DateTime(1900, 1, 1), new DateTime(2100, 1, 1)),
            _ => (new DateTime(date.Year, 1, 1), new DateTime(date.Year + 1, 1, 1))
        };
    }

    public async Task SaveTagsAsync()
    {
        foreach (var tag in Tags)
        {
            await _repository.SaveTagAsync(tag);
        }

        await RefreshCalendarAsync();
        Status = "タグ設定を保存しました。";
    }

    public async Task SaveApplicationSettingsAsync(
        int startupTabIndex,
        bool confirmBeforeDelete,
        bool closeButtonExitsApplication,
        bool defaultNewEventIsAllDay)
    {
        _settings.StartupTabIndex = AppSettingsNormalizer.NormalizeTabIndex(startupTabIndex);
        _settings.ConfirmBeforeDelete = confirmBeforeDelete;
        _settings.CloseButtonExitsApplication = closeButtonExitsApplication;
        _settings.DefaultNewEventIsAllDay = defaultNewEventIsAllDay;
        await SaveApplicationSettingsAsync(_settings);
    }

    public AppSettings CreateSettingsSnapshot()
    {
        return JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_settings)) ?? new AppSettings();
    }

    public async Task SaveApplicationSettingsAsync(AppSettings settings)
    {
        _settings = AppSettingsNormalizer.Normalize(settings);
        SelectedTabIndex = _settings.StartupTabIndex;
        SelectedTodoTabIndex = _settings.StartupTodoTabIndex;
        CurrentViewMode = _settings.StartupCalendarViewMode;
        await _repository.SaveSettingsAsync(_settings);

        foreach (var propertyName in new[]
        {
            nameof(StartupTabIndex), nameof(StartupCalendarViewMode), nameof(ConfirmBeforeDelete),
            nameof(CloseButtonExitsApplication), nameof(DefaultNewEventIsAllDay),
            nameof(HideMainWindowWhileEditingSchedule), nameof(ReuseLastScheduleInput),
            nameof(DefaultScheduleReminderMinutes), nameof(CalendarLabelFontSize),
            nameof(SideListFontSize), nameof(WindowOpacity), nameof(WeekdayHeaders),
            nameof(EnableReminderSound), nameof(ReminderSoundFilePath),
            nameof(ReminderSoundVolume), nameof(EventColorOptions)
        })
        {
            OnPropertyChanged(propertyName);
        }

        await RefreshCalendarAsync();
        Status = "アプリ設定を保存しました。";
    }

    public async Task<IReadOnlyList<string>> LoadScheduleTitleHistoryAsync()
    {
        await ReloadScheduleHistoryAsync();
        return _scheduleTitleHistory;
    }

    public async Task<IReadOnlyList<string>> LoadScheduleLocationHistoryAsync()
    {
        await ReloadScheduleHistoryAsync();
        return _scheduleLocationHistory;
    }

    public async Task ClearScheduleTitleHistoryAsync()
    {
        await _repository.SaveSettingValueAsync(ScheduleTitleHistoryKey, null);
        _scheduleTitleHistory = [];
    }

    public async Task ClearScheduleLocationHistoryAsync()
    {
        await _repository.SaveSettingValueAsync(ScheduleLocationHistoryKey, null);
        _scheduleLocationHistory = [];
    }

    public async Task DeleteSelectedEventAsync(RecurrenceEditScope? recurrenceScope = null)
    {
        await DeleteEventWithRecurrenceAsync(recurrenceScope);
    }

    public async Task<BackupResult> BackupAllCalendarsAsync(string backupZipPath)
    {
        await _repository.InitializeAsync();
        var result = await _backupService.CreateBackupAsync(_repository.DatabasePath, backupZipPath);
        Status = $"バックアップを作成しました: {Path.GetFileName(result.BackupPath)}";
        return result;
    }

    public async Task<RestoreResult> RestoreAllCalendarsAsync(string backupZipPath)
    {
        var result = await _backupService.RestoreBackupAsync(backupZipPath, _repository.DatabasePath);
        await InitializeAsync();
        Status = "バックアップからリストアしました。Google認証は必要に応じて再実行してください。";
        return result;
    }

    public async Task<CalendarCsvExportResult> ExportCurrentYearCsvAsync(string csvPath)
    {
        var events = await LoadYearEventsAsync(CurrentMonth);
        var result = await _csvService.ExportAsync(events, csvPath);
        Status = $"CSVへエクスポートしました: {result.ExportedCount} 件";
        return result;
    }

    public async Task<CalendarCsvImportResult> ImportCsvAsync(string csvPath)
    {
        var result = await _csvService.ImportAsync(csvPath);
        foreach (var calendarEvent in result.Events)
        {
            await _repository.SaveEventAsync(calendarEvent);
        }

        await RefreshCalendarAsync();
        Status = result.Errors.Count == 0
            ? $"CSVからインポートしました: {result.Events.Count} 件"
            : $"CSVから {result.Events.Count} 件をインポートしました。エラー {result.Errors.Count} 件。";
        return result;
    }

    public Task<FavGCalImportAnalysis> AnalyzeFavGCalSchedulerImportAsync(string sourceFolder)
    {
        return _favGCalImportService.AnalyzeAsync(sourceFolder);
    }

    public async Task<FavGCalImportResult> ImportFavGCalSchedulerAsync(FavGCalImportOptions options)
    {
        var mappedCalendarIds = options.CalendarMappings.Values
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var calendarId in mappedCalendarIds)
        {
            if (!AvailableCalendars.Any(item => item.Id == calendarId))
            {
                AvailableCalendars.Add(new GoogleCalendarSelectionItem
                {
                    Id = calendarId,
                    Summary = calendarId,
                    IsSelected = true
                });
            }
        }

        foreach (var calendar in AvailableCalendars)
        {
            if (mappedCalendarIds.Contains(calendar.Id, StringComparer.Ordinal))
            {
                calendar.IsSelected = true;
            }
        }

        if (options.ImportSettings)
        {
            ApplyFavGCalSchedulerSettings(options.SourceFolder);
        }

        await SaveOAuthPathAsync();
        if (options.VerifyGoogleEventsBeforeImport
            && mappedCalendarIds.Length > 0
            && !string.IsNullOrWhiteSpace(_settings.OAuthClientJsonPath)
            && File.Exists(_settings.OAuthClientJsonPath))
        {
            Status = "Googleカレンダーから既存予定を確認しています...";
            await _syncService.PullAsync(_settings, mappedCalendarIds);
        }

        var result = await _favGCalImportService.ImportAsync(options);
        if (options.ImportSettings)
        {
            await SaveApplicationSettingsAsync(_settings);
        }

        await ReloadAvailableCalendarsAsync();
        await RefreshCalendarAsync();
        Status = $"FavGCalSchedulerデータを取り込みました: 追加 {result.ImportedCount} 件、既存紐付け {result.LinkedExistingGoogleCount} 件、重複スキップ {result.SkippedDuplicateCount} 件、ToDo内容修復 {result.CorrectedTodoDescriptionCount} 件";
        return result;
    }

    public async Task SetOAuthClientJsonPathAsync(string path)
    {
        OAuthClientJsonPath = path;
        _settings.OAuthClientJsonPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        await _repository.SaveSettingsAsync(_settings);
        await ReloadAvailableCalendarsAsync();
    }

    public async Task AuthorizeGoogleAsync()
    {
        await AuthorizeAsync();
    }

    public async Task<SyncPreview> CreateSyncPreviewAsync()
    {
        await SaveOAuthPathAsync();
        return await _syncService.PreviewAsync(_settings);
    }

    public async Task<SyncResult?> SynchronizeManuallyAsync()
    {
        return await SynchronizeAsync(reportErrors: true, SyncInvocationKind.Manual);
    }

    public void SetManualSyncPreviewConfirmation(Func<SyncPreview, Task<bool>>? confirmManualSyncPreviewAsync)
    {
        _confirmManualSyncPreviewAsync = confirmManualSyncPreviewAsync;
    }

    public void SetWindowCommandHandlers(
        Func<Task> showAddScheduleAsync,
        Func<Task> showAddTodoAsync,
        Func<Task> backupAllCalendarsAsync,
        Func<Task> restoreAllCalendarsAsync,
        Func<Task> importFavGCalSchedulerAsync,
        Func<Task> importCsvAsync,
        Func<Task> exportCsvAsync,
        Func<Task> showScheduleListAsync,
        Func<Task> showSearchAsync,
        Func<Task> showSyncDiagnosticsAsync,
        Func<Task> showSettingsAsync,
        Func<Task> showReminderHistoryAsync,
        Func<Task> showAboutAsync,
        Func<Task>? showMonthJumpAsync = null)
    {
        _showAddScheduleAsync = showAddScheduleAsync;
        _showAddTodoAsync = showAddTodoAsync;
        _backupAllCalendarsAsync = backupAllCalendarsAsync;
        _restoreAllCalendarsAsync = restoreAllCalendarsAsync;
        _importFavGCalSchedulerAsync = importFavGCalSchedulerAsync;
        _importCsvAsync = importCsvAsync;
        _exportCsvAsync = exportCsvAsync;
        _showScheduleListAsync = showScheduleListAsync;
        _showSearchAsync = showSearchAsync;
        _showSyncDiagnosticsAsync = showSyncDiagnosticsAsync;
        _showSettingsAsync = showSettingsAsync;
        _showReminderHistoryAsync = showReminderHistoryAsync;
        _showAboutAsync = showAboutAsync;
        _showMonthJumpAsync = showMonthJumpAsync;
    }

    private static Task InvokeWindowCommandAsync(Func<Task>? handler) => handler?.Invoke() ?? Task.CompletedTask;

    public async Task<SyncResult?> SynchronizeManuallyWithPreviewAsync()
    {
        var settings = CreateSettingsSnapshot();
        if (settings.ShowSyncPreviewBeforeManualSync && _confirmManualSyncPreviewAsync is not null)
        {
            var preview = await CreateSyncPreviewAsync();
            if (!await _confirmManualSyncPreviewAsync(preview))
            {
                Status = "同期をキャンセルしました。";
                return null;
            }
        }

        return await SynchronizeManuallyAsync();
    }

    public async Task<SyncResult> SynchronizeDirtyOnlyAsync()
    {
        var dirtyIds = (await _repository.LoadDirtyEventsAsync())
            .Select(item => item.Id)
            .ToArray();
        if (dirtyIds.Length == 0)
        {
            var empty = SyncResult.Empty("未同期の予定はありません。");
            Status = empty.Message;
            await RefreshOperationalStatusAsync(null);
            return empty;
        }

        var result = await ResyncDirtyItemsAsync(dirtyIds);
        await RefreshOperationalStatusAsync(null);
        return result;
    }

    public async Task<SyncDiagnosticsSnapshot> LoadSyncDiagnosticsAsync()
    {
        await SaveOAuthPathAsync();
        return await _syncService.LoadDiagnosticsAsync(_settings);
    }

    public async Task<int> RefreshGoogleReminderMetadataAsync()
    {
        await SaveOAuthPathAsync();
        var now = DateTimeOffset.Now;
        var updated = await _syncService.RefreshReminderMetadataAsync(
            _settings,
            now.AddDays(-1),
            now.AddDays(30));
        Status = $"Google通知設定を再取得しました: {updated} 件";
        await RefreshCalendarAsync();
        await RefreshOperationalStatusAsync(null);
        return updated;
    }

    public async Task<CalendarEvent?> FindEventByIdAsync(string localId)
    {
        return await _repository.FindEventByIdAsync(localId);
    }

    public async Task<SyncResult> ResyncDirtyItemsAsync(IReadOnlyCollection<string> localIds)
    {
        await SaveOAuthPathAsync();
        var result = await _syncService.SyncDirtyEventsAsync(_settings, localIds.ToHashSet(StringComparer.Ordinal));
        await RefreshCalendarAsync();
        Status = $"{result.Message} / 未同期残数 {(await _repository.LoadDirtyEventsAsync()).Count}";
        return result;
    }

    public async Task<SyncResult> ResyncFailedItemsAsync(IReadOnlyCollection<string> localIds)
    {
        var dirtyIds = (await _repository.LoadDirtyEventsAsync())
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var targets = localIds
            .Where(id => dirtyIds.Contains(id))
            .ToArray();
        if (targets.Length == 0)
        {
            var empty = SyncResult.Empty("再同期対象の失敗データは現在 dirty ではありません。");
            Status = empty.Message;
            return empty;
        }

        return await ResyncDirtyItemsAsync(targets);
    }

    public async Task<int> MarkDirtyItemsSyncedAsync(IReadOnlyCollection<string> localIds)
    {
        await CreateDiagnosticsBulkBackupAsync();
        var updated = await _repository.MarkSyncedByIdsAsync(localIds);
        await RefreshCalendarAsync();
        Status = $"選択した未同期データを同期済み扱いにしました: {updated} 件";
        return updated;
    }

    public async Task<SyncResult> DiscardLocalChangesAsync(IReadOnlyCollection<string> localIds)
    {
        await CreateDiagnosticsBulkBackupAsync();
        await SaveOAuthPathAsync();
        var result = await _syncService.DiscardLocalChangesAsync(_settings, localIds.ToHashSet(StringComparer.Ordinal));
        await RefreshCalendarAsync();
        Status = result.Message;
        return result;
    }

    public async Task<BackupResult> CreateDiagnosticsBulkBackupAsync()
    {
        await _repository.InitializeAsync();
        var backupPath = Path.Combine(
            AppPaths.AppDataDirectory,
            "backups",
            $"diagnostics-bulk-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        return await _backupService.CreateBackupAsync(_repository.DatabasePath, backupPath);
    }

    public async Task ClearSyncDiagnosticsAsync()
    {
        await _syncService.ClearSyncDiagnosticsAsync();
    }

    public async Task RunAutomaticSyncIfDueAsync()
    {
        if (_settings.AutomaticSyncIntervalMinutes is not int interval
            || !CanSynchronize()
            || _settings.LastAutomaticSyncAt is { } lastSync
               && DateTimeOffset.Now - lastSync < TimeSpan.FromMinutes(interval))
        {
            return;
        }

        await SynchronizeAsync(reportErrors: false, SyncInvocationKind.Automatic);
    }

    private async Task ReloadTagsAsync()
    {
        Tags.Clear();
        foreach (var tag in await _repository.LoadTagsAsync())
        {
            Tags.Add(tag);
        }
    }

    public async Task ReloadAvailableCalendarsAsync()
    {
        var calendars = await LoadAvailableCalendarsCoreAsync();

        AvailableCalendars.Clear();
        foreach (var calendar in calendars)
        {
            AvailableCalendars.Add(calendar);
        }

        RefreshCalendarNames();
        if (!AvailableCalendars.Any(item => item.IsSelected) && AvailableCalendars.Count > 0)
        {
            AvailableCalendars[0].IsSelected = true;
        }

        EditorCalendarId = ResolveEditorCalendarId();
    }

    public async Task ApplyCalendarSelectionAsync()
    {
        if (Interlocked.Exchange(ref _calendarSelectionInProgress, 1) != 0)
        {
            Interlocked.Exchange(ref _calendarSelectionRerunRequested, 1);
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _calendarSelectionRerunRequested, 0);
                await ApplyCalendarSelectionCoreAsync();
            }
            while (Interlocked.Exchange(ref _calendarSelectionRerunRequested, 0) != 0);
        }
        finally
        {
            Interlocked.Exchange(ref _calendarSelectionInProgress, 0);
        }
    }

    private async Task ApplyCalendarSelectionCoreAsync()
    {
        if (!AvailableCalendars.Any(item => item.IsSelected) && AvailableCalendars.Count > 0)
        {
            AvailableCalendars[0].IsSelected = true;
        }

        RefreshCalendarNames();
        _settings.VisibleCalendarIds = AvailableCalendars.Where(item => item.IsSelected).Select(item => item.Id).ToList();
        _settings.ActiveCalendarId = _settings.VisibleCalendarIds.FirstOrDefault() ?? ResolveEditorCalendarId();
        if (!_settings.VisibleCalendarIds.Contains(EditorCalendarId, StringComparer.Ordinal))
        {
            EditorCalendarId = _settings.ActiveCalendarId;
        }

        try
        {
            await _repository.SaveSettingsAsync(_settings);
        }
        catch (Exception ex)
        {
            Status = $"表示カレンダー設定を保存できませんでした: {ex.Message}";
            throw new InvalidOperationException(Status, ex);
        }

        try
        {
            await RefreshCalendarAsync();
        }
        catch (Exception ex)
        {
            Status = $"表示カレンダーは保存しましたが、カレンダー再読込に失敗しました: {ex.Message}";
            throw new InvalidOperationException(Status, ex);
        }

        try
        {
            await RefreshOperationalStatusAsync(null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            Status = $"表示カレンダーを更新しました。状態表示の更新に失敗しました: {ex.Message}";
        }
    }

    private void StartRefreshCalendar()
    {
        _ = RefreshCalendarSafelyAsync(BeginCalendarRefresh(saveDisplayMonth: true, refreshTodos: true));
    }

    private async Task RefreshCalendarSafelyAsync(CalendarRefreshRequest request)
    {
        try
        {
            await RefreshCalendarPreservingSelectionAsync(request);
        }
        catch (OperationCanceledException)
        {
            // A newer navigation request superseded this refresh.
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            Status = "カレンダー表示の更新に失敗しました。";
        }
    }

    private async Task RefreshCalendarAsync()
    {
        await RefreshCalendarAsync(invalidateCache: true, refreshTodos: true);
    }

    private async Task RefreshCalendarAsync(bool invalidateCache, bool refreshTodos)
    {
        if (invalidateCache)
        {
            InvalidateCalendarCache();
        }

        await RefreshCalendarAsync(BeginCalendarRefresh(saveDisplayMonth: true, refreshTodos));
    }

    private async Task RefreshCalendarAsync(CalendarRefreshRequest request)
    {
        var selectedId = SelectedEvent?.Id;
        await RefreshCalendarCoreAsync(request);
        if (string.IsNullOrWhiteSpace(selectedId) || !IsLatestCalendarRefresh(request))
        {
            return;
        }

        var refreshed = _visibleEvents.FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal))
            ?? _storedEvents.FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal));
        if (refreshed is not null)
        {
            SelectedEvent = refreshed;
        }
    }

    private async Task RefreshCalendarCoreAsync(CalendarRefreshRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        if (request.SaveDisplayMonth)
        {
            _settings.DisplayMonth = request.Month;
            BeforeSaveDisplayMonth?.Invoke(request.Month);
            await _repository.SaveSettingsAsync(_settings);
            request.CancellationToken.ThrowIfCancellationRequested();
        }

        var snapshot = TryGetCalendarCache(request.Month) ?? await LoadCalendarSnapshotAsync(request.Month, request.CancellationToken);
        if (!IsLatestCalendarRefresh(request))
        {
            return;
        }

        ApplyCalendarSnapshot(snapshot, request.PendingSelectedDate, clearPendingSelectedDate: true);
        StoreCalendarCache(snapshot);
        if (request.RefreshTodos)
        {
            BeforeRefreshTodos?.Invoke();
            await RefreshTodosAsync();
        }
        if (IsLatestCalendarRefresh(request))
        {
            _ = PrefetchAdjacentMonthsAsync(request);
        }
    }

    private async Task<CalendarRefreshSnapshot> LoadCalendarSnapshotAsync(DateTime month, CancellationToken cancellationToken)
    {
        if (BeforeLoadCalendarSnapshotAsync is not null)
        {
            await BeforeLoadCalendarSnapshotAsync(month, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var context = CreateCalendarSnapshotBuildContext();
        var (gridStart, gridEnd) = DateRangeHelper.MonthGridRange(month, context.WeekStartsOnMonday);
        var storedEvents = await _repository.LoadEventsAsync(
            new DateTimeOffset(gridStart),
            new DateTimeOffset(gridEnd),
            includeDeleted: true,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await Task.Run(
            () => BuildCalendarSnapshot(month, gridStart, gridEnd, storedEvents, context, cancellationToken),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot;
    }

    private CalendarRefreshSnapshot BuildCalendarSnapshot(
        DateTime month,
        DateTime gridStart,
        DateTime gridEnd,
        IReadOnlyList<CalendarEvent> storedEvents,
        CalendarSnapshotBuildContext context,
        CancellationToken cancellationToken)
    {
        BeforeBuildCalendarSnapshot?.Invoke(month, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var calendarEvents = RecurrenceExpansionService
            .ExpandForRange(storedEvents, new DateTimeOffset(gridStart), new DateTimeOffset(gridEnd))
            .Where(item => IsInVisibleCalendar(item, context))
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var visibleEvents = calendarEvents.Where(item => IsVisible(item, context)).ToArray();
        ApplyDisplayColors(visibleEvents, context);
        return new CalendarRefreshSnapshot(month, gridStart, gridEnd, storedEvents, calendarEvents, visibleEvents);
    }

    private void ApplyCalendarSnapshot(
        CalendarRefreshSnapshot snapshot,
        DateTime? pendingSelectedDate,
        bool clearPendingSelectedDate)
    {
        _storedEvents = snapshot.StoredEvents;
        _dayDirectiveEvents = snapshot.DayDirectiveEvents;
        _visibleEvents = snapshot.VisibleEvents;

        EnsureCalendarDayCapacity((snapshot.GridEnd - snapshot.GridStart).Days);
        var eventsByDate = CreateEventsByDateIndex(snapshot);
        var index = 0;
        for (var date = snapshot.GridStart; date < snapshot.GridEnd; date = date.AddDays(1), index++)
        {
            var day = CalendarDays[index];
            UpdateCalendarDayShell(day, date, clearEvents: true);
            day.IsWorkdayOverride = TagService.HasWorkdayOverride(_dayDirectiveEvents, date);
            day.IsHoliday = TagService.HasHolidayWithoutWorkdayOverride(_dayDirectiveEvents, date);
            if (!eventsByDate.TryGetValue(date.Date, out var eventsForDate))
            {
                continue;
            }

            foreach (var calendarEvent in eventsForDate.Take(5))
            {
                day.Events.Add(calendarEvent);
            }

            day.HiddenEventCount = Math.Max(0, eventsForDate.Count - day.Events.Count);
        }
        CalendarSegmentLayoutService.PopulateSegments(CalendarDays, _visibleEvents);

        CalendarDay? selectedDay;
        DateTime? visibleAnchorDate;
        if (pendingSelectedDate is { } selectedDate)
        {
            selectedDay = CalendarDays.FirstOrDefault(d => d.Date == selectedDate.Date) ?? FindOrCreateCalendarDay(selectedDate.Date);
            visibleAnchorDate = selectedDay.Date;
            if (clearPendingSelectedDate)
            {
                _pendingSelectedDate = null;
            }
        }
        else
        {
            selectedDay = SelectedDay is not null
                ? CalendarDays.FirstOrDefault(d => d.Date == SelectedDay.Date) ?? FindOrCreateCalendarDay(SelectedDay.Date)
                : CalendarDays.FirstOrDefault(d => d.Date == DateTime.Today) ?? CalendarDays.FirstOrDefault();
            visibleAnchorDate = selectedDay?.Date;
        }

        RefreshVisibleCalendarDays(visibleAnchorDate);
        SelectedDay = selectedDay;
        UpdateSegmentSelection();
        RefreshSelectedDayEvents();
        RefreshSevenDayEvents();
    }

    private static Dictionary<DateTime, List<CalendarEvent>> CreateEventsByDateIndex(CalendarRefreshSnapshot snapshot)
    {
        var result = new Dictionary<DateTime, List<CalendarEvent>>();
        var gridLastDate = snapshot.GridEnd.AddDays(-1).Date;
        foreach (var calendarEvent in snapshot.VisibleEvents)
        {
            var firstDate = MaxDate(snapshot.GridStart.Date, calendarEvent.Start.Date);
            var lastDate = MinDate(gridLastDate, calendarEvent.End.Date);
            if (lastDate < firstDate)
            {
                lastDate = firstDate;
            }

            for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
            {
                if (!DateRangeHelper.OccursOn(calendarEvent, date))
                {
                    continue;
                }

                if (!result.TryGetValue(date, out var eventsForDate))
                {
                    eventsForDate = [];
                    result[date] = eventsForDate;
                }

                eventsForDate.Add(calendarEvent);
            }
        }

        return result;
    }

    private static DateTime MaxDate(DateTime left, DateTime right) => left >= right ? left : right;

    private static DateTime MinDate(DateTime left, DateTime right) => left <= right ? left : right;

    private async Task RefreshCalendarPreservingSelectionAsync(CalendarRefreshRequest request)
    {
        await RefreshCalendarAsync(request);
    }

    private void ScheduleCalendarRefreshAfterNavigation()
    {
        CancelActiveCalendarRefresh();
        _deferredCalendarRefreshCts?.Cancel();
        _deferredCalendarRefreshCts = new CancellationTokenSource();
        _ = RefreshCalendarAfterNavigationDelayAsync(_deferredCalendarRefreshCts.Token);
    }

    private async Task RefreshCalendarAfterNavigationDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(NavigationRefreshDelay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshCalendarSafelyAsync(BeginCalendarRefresh(saveDisplayMonth: true, refreshTodos: false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelActiveCalendarRefresh()
    {
        _calendarRefreshCts?.Cancel();
        Interlocked.Increment(ref _refreshGeneration);
    }

    private CalendarRefreshRequest BeginCalendarRefresh(bool saveDisplayMonth, bool refreshTodos)
    {
        _deferredCalendarRefreshCts?.Cancel();
        _calendarRefreshCts?.Cancel();
        _calendarRefreshCts = new CancellationTokenSource();
        var generation = Interlocked.Increment(ref _refreshGeneration);
        return new CalendarRefreshRequest(
            generation,
            CurrentMonth,
            _pendingSelectedDate,
            saveDisplayMonth,
            refreshTodos,
            _calendarRefreshCts.Token);
    }

    private bool IsLatestCalendarRefresh(CalendarRefreshRequest request)
    {
        return request.Generation == _refreshGeneration
            && !request.CancellationToken.IsCancellationRequested
            && CurrentMonth.Year == request.Month.Year
            && CurrentMonth.Month == request.Month.Month;
    }

    private void ShowImmediateCalendarShellForMonth(DateTime month)
    {
        if (TryGetCalendarCache(month) is { } snapshot)
        {
            ApplyCalendarSnapshot(snapshot, _pendingSelectedDate, clearPendingSelectedDate: false);
            return;
        }

        var (gridStart, gridEnd) = DateRangeHelper.MonthGridRange(month, _settings.WeekStartsOnMonday);
        EnsureCalendarDayCapacity((gridEnd - gridStart).Days);
        var index = 0;
        for (var date = gridStart; date < gridEnd; date = date.AddDays(1), index++)
        {
            UpdateCalendarDayShell(CalendarDays[index], date, clearEvents: true);
        }

        var anchor = _pendingSelectedDate?.Date ?? _navigationAnchorDate?.Date ?? month.Date;
        RefreshVisibleCalendarDays(anchor);
        SetSelectedDayForImmediateNavigation(FindOrCreateCalendarDay(anchor));
        SelectedDayEvents.Clear();
        SevenDayEvents.Clear();
    }

    private void EnsureCalendarDayCapacity(int count)
    {
        while (CalendarDays.Count < count)
        {
            CalendarDays.Add(new CalendarDay());
        }

        while (CalendarDays.Count > count)
        {
            CalendarDays.RemoveAt(CalendarDays.Count - 1);
        }
    }

    private void UpdateCalendarDayShell(CalendarDay day, DateTime date, bool clearEvents)
    {
        day.Date = date;
        day.IsCurrentMonth = date.Month == CurrentMonth.Month;
        day.IsWorkdayOverride = false;
        day.IsHoliday = false;
        if (clearEvents)
        {
            day.Events.Clear();
            day.HiddenEventCount = 0;
            day.Segments.Clear();
        }
    }

    private void SetSelectedDayForImmediateNavigation(CalendarDay? day)
    {
        if (ReferenceEquals(_selectedDay, day))
        {
            return;
        }

        _selectedDay = day;
        if (day is not null)
        {
            _navigationAnchorDate = day.Date;
            StartDate = day.Date;
            EndDate = day.Date;
        }

        OnPropertyChanged(nameof(SelectedDay));
        OnPropertyChanged(nameof(CurrentPeriodTitle));
        OnPropertyChanged(nameof(CalendarStatusText));
    }

    private async Task PrefetchAdjacentMonthsAsync(CalendarRefreshRequest request)
    {
        try
        {
            await Task.Delay(120, request.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"Calendar prefetch canceled before start: generation={request.Generation}");
            return;
        }

        var context = CreateCalendarSnapshotBuildContext();
        foreach (var month in new[] { request.Month.AddMonths(-1), request.Month.AddMonths(1) })
        {
            try
            {
                request.CancellationToken.ThrowIfCancellationRequested();
                var key = CreateCalendarCacheKey(month, context);
                if (_calendarCache.ContainsKey(key))
                {
                    continue;
                }

                Debug.WriteLine($"Calendar prefetch start: {month:yyyy-MM}, generation={request.Generation}");
                var (gridStart, gridEnd) = DateRangeHelper.MonthGridRange(month, context.WeekStartsOnMonday);
                var storedEvents = await _repository.LoadEventsAsync(
                    new DateTimeOffset(gridStart),
                    new DateTimeOffset(gridEnd),
                    includeDeleted: true,
                    request.CancellationToken);
                request.CancellationToken.ThrowIfCancellationRequested();
                var snapshot = await Task.Run(
                    () => BuildCalendarSnapshot(month, gridStart, gridEnd, storedEvents, context, request.CancellationToken),
                    request.CancellationToken);
                request.CancellationToken.ThrowIfCancellationRequested();
                if (!IsLatestCalendarRefresh(request) || _calendarCache.ContainsKey(key))
                {
                    Debug.WriteLine($"Calendar prefetch discarded: {month:yyyy-MM}, generation={request.Generation}");
                    continue;
                }

                StoreCalendarCache(snapshot, key);
                Debug.WriteLine($"Calendar prefetch complete: {month:yyyy-MM}, generation={request.Generation}");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"Calendar prefetch canceled: generation={request.Generation}");
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return;
            }
        }
    }

    private CalendarCacheKey CreateCalendarCacheKey(DateTime month)
    {
        return CreateCalendarCacheKey(month, CreateCalendarSnapshotBuildContext());
    }

    private CalendarCacheKey CreateCalendarCacheKey(DateTime month, CalendarSnapshotBuildContext context)
    {
        var normalizedMonth = new DateTime(month.Year, month.Month, 1);
        var visibleCalendars = string.Join("|", context.VisibleCalendarIds.OrderBy(id => id, StringComparer.Ordinal));
        return new CalendarCacheKey(normalizedMonth, context.WeekStartsOnMonday, visibleCalendars);
    }

    private CalendarRefreshSnapshot? TryGetCalendarCache(DateTime month)
    {
        var key = CreateCalendarCacheKey(month);
        if (!_calendarCache.TryGetValue(key, out var snapshot))
        {
            return null;
        }

        Debug.WriteLine($"Calendar cache hit: {key.Month:yyyy-MM}, weekStartsOnMonday={key.WeekStartsOnMonday}, calendars={key.VisibleCalendarIds}");
        return snapshot;
    }

    private void StoreCalendarCache(CalendarRefreshSnapshot snapshot)
    {
        StoreCalendarCache(snapshot, CreateCalendarCacheKey(snapshot.Month));
    }

    private void StoreCalendarCache(CalendarRefreshSnapshot snapshot, CalendarCacheKey key)
    {
        _calendarCache[key] = snapshot;
        if (_calendarCache.Count <= 5)
        {
            return;
        }

        var keep = new HashSet<CalendarCacheKey>
        {
            CreateCalendarCacheKey(CurrentMonth.AddMonths(-1)),
            CreateCalendarCacheKey(CurrentMonth),
            CreateCalendarCacheKey(CurrentMonth.AddMonths(1))
        };
        foreach (var cacheKey in _calendarCache.Keys.Where(candidate => !keep.Contains(candidate)).ToArray())
        {
            _calendarCache.Remove(cacheKey);
            if (_calendarCache.Count <= 5)
            {
                break;
            }
        }
    }

    private void InvalidateCalendarCache()
    {
        _calendarCache.Clear();
    }

    private void RefreshSelectedDayEvents()
    {
        SelectedDayEvents.Clear();
        if (SelectedDay is null)
        {
            return;
        }

        foreach (var calendarEvent in _visibleEvents.Where(e => DateRangeHelper.OccursOn(e, SelectedDay.Date)).OrderBy(e => e.Start))
        {
            SelectedDayEvents.Add(calendarEvent);
        }
    }

    private void RefreshSevenDayEvents()
    {
        SevenDayEvents.Clear();
        var start = (SelectedDay?.Date ?? DateTime.Today).Date;
        var end = start.AddDays(7);
        foreach (var calendarEvent in _visibleEvents.Where(e => e.Start.Date < end && e.End.Date >= start).OrderBy(e => e.Start))
        {
            SevenDayEvents.Add(calendarEvent);
        }
    }

    private async Task RefreshTodosAsync()
    {
        TodoEvents.Clear();
        CompletedTodoEvents.Clear();
        var events = (await _repository.LoadTodoEventsAsync()).Where(IsVisible).ToArray();
        ApplyDisplayColors(events);

        foreach (var item in events
                     .Where(item => !item.IsTodoDone && TodoDisplayFilter.IsWithinDisplayPeriod(item, _settings.IncompleteTodoDisplayPeriodMonths, DateTime.Today))
                     .Where(PassesTodoQuickFilter)
                     .OrderBy(item => item.Start)
                     .ThenBy(item => item.TodoPriority)
                     .Take(100))
        {
            TodoEvents.Add(item);
        }

        foreach (var item in events
                     .Where(item => item.IsTodoDone && TodoDisplayFilter.IsWithinDisplayPeriod(item, _settings.CompletedTodoDisplayPeriodMonths, DateTime.Today))
                     .OrderBy(item => Math.Abs((item.Start.Date - DateTime.Today).Days))
                     .ThenBy(item => item.Start.Date)
                     .ThenByDescending(item => item.UpdatedAt)
                     .ThenBy(item => item.Title, StringComparer.CurrentCulture)
                     .Take(100))
        {
            CompletedTodoEvents.Add(item);
        }
    }

    private bool PassesTodoQuickFilter(CalendarEvent item)
    {
        var today = DateTime.Today;
        return TodoQuickFilter switch
        {
            TodoQuickFilter.Today => item.Start.Date == today,
            TodoQuickFilter.Overdue => item.Start.Date < today,
            TodoQuickFilter.ThisWeek => item.Start.Date >= today && item.Start.Date < today.AddDays(7),
            TodoQuickFilter.HighPriority => string.Equals(item.TodoPriority, "A", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(item.TodoPriority, "B", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static string FormatReminderStatus(ReminderMonitoringSnapshot value) =>
        $"通知監視: {(value.IsRunning ? "起動中" : "停止中")} / 次回 {FormatStatusDate(value.NextCheckAt)}";

    private static string FormatStatusDate(DateTimeOffset? value) => value?.ToString("MM/dd HH:mm") ?? "未定";

    private static string BuildLastErrorStatus(SyncDiagnosticsSnapshot? sync, ReminderMonitoringSnapshot? reminder)
    {
        if (reminder?.LastError is { Length: > 0 } reminderError)
        {
            return $"通知エラー: {TrimStatusError(reminderError)}";
        }

        if (sync?.LastResult is { Failed: > 0 } result)
        {
            return $"同期エラー: {result.Failed} 件";
        }

        return "";
    }

    private static string TrimStatusError(string value)
    {
        var normalized = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 80 ? normalized : normalized[..80] + "...";
    }

    private async Task ReloadScheduleHistoryAsync()
    {
        _scheduleTitleHistory = DeserializeHistory(await _repository.LoadSettingValueAsync(ScheduleTitleHistoryKey));
        _scheduleLocationHistory = DeserializeHistory(await _repository.LoadSettingValueAsync(ScheduleLocationHistoryKey));
    }

    private async Task RecordScheduleHistoryAsync(CalendarEvent calendarEvent)
    {
        _scheduleTitleHistory = AddHistoryValue(_scheduleTitleHistory, calendarEvent.Title);
        _scheduleLocationHistory = AddHistoryValue(_scheduleLocationHistory, calendarEvent.Location);
        await _repository.SaveSettingValueAsync(ScheduleTitleHistoryKey, JsonSerializer.Serialize(_scheduleTitleHistory));
        await _repository.SaveSettingValueAsync(ScheduleLocationHistoryKey, JsonSerializer.Serialize(_scheduleLocationHistory));
    }

    private void ApplyDisplayColors(IEnumerable<CalendarEvent> events)
    {
        ApplyDisplayColors(events, CreateCalendarSnapshotBuildContext());
    }

    private void ApplyDisplayColors(IEnumerable<CalendarEvent> events, CalendarSnapshotBuildContext context)
    {
        foreach (var calendarEvent in events)
        {
            var colors = TagService.ResolveDisplayColors(calendarEvent, context.EventColorPalette);
            calendarEvent.DisplayColor = colors.Background;
            calendarEvent.DisplayForegroundColor = colors.Foreground;
            calendarEvent.ToolTipText = CalendarEventToolTipFormatter.Format(
                calendarEvent,
                context.CalendarNames.GetValueOrDefault(calendarEvent.CalendarId));
        }
    }

    private bool IsVisible(CalendarEvent calendarEvent)
    {
        return IsInVisibleCalendar(calendarEvent)
            && !TagService.IsDayCellDirective(calendarEvent);
    }

    private static bool IsVisible(CalendarEvent calendarEvent, CalendarSnapshotBuildContext context)
    {
        return IsInVisibleCalendar(calendarEvent, context)
            && !TagService.IsDayCellDirective(calendarEvent);
    }

    private bool IsInVisibleCalendar(CalendarEvent calendarEvent) =>
        AvailableCalendars.Count == 0
        || AvailableCalendars.Any(item => item.IsSelected && item.Id == calendarEvent.CalendarId);

    private static bool IsInVisibleCalendar(CalendarEvent calendarEvent, CalendarSnapshotBuildContext context) =>
        context.VisibleCalendarIds.Count == 0
        || context.VisibleCalendarIds.Contains(calendarEvent.CalendarId);

    private CalendarSnapshotBuildContext CreateCalendarSnapshotBuildContext()
    {
        return new CalendarSnapshotBuildContext(
            _settings.WeekStartsOnMonday,
            AvailableCalendars
                .Where(item => item.IsSelected)
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal),
            AvailableCalendars.ToDictionary(item => item.Id, item => item.Summary, StringComparer.Ordinal),
            _eventColorPalette.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    private void NavigatePrimary(int direction)
    {
        var anchor = GetNavigationAnchorDate();
        var target = CurrentViewMode switch
        {
            CalendarViewMode.Month => anchor.AddMonths(direction),
            CalendarViewMode.Week => anchor.AddDays(direction * 7),
            CalendarViewMode.Day => anchor.AddDays(direction),
            _ => anchor
        };
        NavigateToDate(target);
    }

    private void NavigateSecondary(int direction)
    {
        var anchor = GetNavigationAnchorDate();
        var target = CurrentViewMode switch
        {
            CalendarViewMode.Month => anchor.AddYears(direction),
            CalendarViewMode.Week => anchor.AddMonths(direction),
            CalendarViewMode.Day => anchor.AddMonths(direction),
            _ => anchor
        };
        NavigateToDate(target);
    }

    private void NavigateToDate(DateTime targetDate)
    {
        _pendingSelectedDate = targetDate.Date;
        _navigationAnchorDate = targetDate.Date;
        SetCurrentMonthWithoutRefreshing(targetDate.Date);
        ShowImmediateCalendarShellForMonth(CurrentMonth);
        ScheduleCalendarRefreshAfterNavigation();
    }

    private DateTime GetNavigationAnchorDate()
    {
        return _pendingSelectedDate?.Date
            ?? _navigationAnchorDate?.Date
            ?? SelectedDay?.Date
            ?? CurrentMonth;
    }

    private void RefreshVisibleCalendarDays(DateTime? anchorDate = null)
    {
        VisibleCalendarDays.Clear();

        var anchor = anchorDate?.Date ?? SelectedDay?.Date ?? CurrentMonth;
        IEnumerable<CalendarDay> days = CalendarVisibleRangeService
            .GetVisibleDates(CurrentViewMode, CalendarDays, anchor, _settings.WeekStartsOnMonday)
            .Select(FindOrCreateCalendarDay);

        foreach (var day in days)
        {
            VisibleCalendarDays.Add(day);
        }
    }

    private CalendarDay FindOrCreateCalendarDay(DateTime date)
    {
        return CalendarDays.FirstOrDefault(day => day.Date == date)
            ?? CreateCalendarDay(date, _dayDirectiveEvents);
    }

    private CalendarDay CreateCalendarDay(DateTime date, IEnumerable<CalendarEvent> events)
    {
        var day = new CalendarDay
        {
            Date = date,
            IsCurrentMonth = date.Month == CurrentMonth.Month,
            IsWorkdayOverride = TagService.HasWorkdayOverride(events, date),
            IsHoliday = TagService.HasHolidayWithoutWorkdayOverride(events, date)
        };

        foreach (var calendarEvent in _visibleEvents.Where(e => DateRangeHelper.OccursOn(e, date)).Take(5))
        {
            day.Events.Add(calendarEvent);
        }

        day.HiddenEventCount = Math.Max(0, _visibleEvents.Count(e => DateRangeHelper.OccursOn(e, date)) - day.Events.Count);

        return day;
    }

    private void SetCurrentMonthWithoutRefreshing(DateTime value)
    {
        _currentMonth = new DateTime(value.Year, value.Month, 1);
        OnPropertyChanged(nameof(CurrentMonth));
        OnPropertyChanged(nameof(MonthTitle));
        OnPropertyChanged(nameof(JapaneseMonthTitle));
        OnPropertyChanged(nameof(CurrentPeriodTitle));
    }

    private void LoadEditor(CalendarEvent? calendarEvent)
    {
        if (calendarEvent is null)
        {
            return;
        }

        EditorCalendarId = calendarEvent.CalendarId;
        Title = calendarEvent.Title;
        Description = calendarEvent.Description ?? "";
        Location = calendarEvent.Location ?? "";
        StartDate = calendarEvent.Start.Date;
        EndDate = calendarEvent.IsAllDay ? calendarEvent.End.Date.AddDays(-1) : calendarEvent.End.Date;
        StartTime = calendarEvent.Start.ToString("HH:mm", CultureInfo.InvariantCulture);
        EndTime = calendarEvent.End.ToString("HH:mm", CultureInfo.InvariantCulture);
        IsAllDay = calendarEvent.IsAllDay;
        ReminderMinutesBeforeStart = calendarEvent.ReminderMinutesBeforeStart;
        EditorColorId = calendarEvent.ColorId;
    }

    private async Task SaveEventWithRecurrenceAsync(RecurrenceEditScope? recurrenceScope)
    {
        var candidate = BuildEditedEventAsync();
        if (candidate is null)
        {
            return;
        }

        if (SelectedEvent is null || recurrenceScope is null)
        {
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            await RecordScheduleHistoryAsync(candidate);
            SelectedEvent = candidate;
            await RefreshCalendarAsync();
            Status = "予定を保存しました。";
            await SyncAfterLocalChangeAsync();
            return;
        }

        switch (recurrenceScope.Value)
        {
            case RecurrenceEditScope.ThisOccurrence:
                await SaveSingleOccurrenceAsync(candidate);
                break;
            case RecurrenceEditScope.ThisAndFollowing:
                await SaveThisAndFollowingAsync(candidate);
                break;
            case RecurrenceEditScope.AllEvents:
                await SaveEntireSeriesAsync(candidate);
                break;
        }

        await RefreshCalendarAsync();
        await RecordScheduleHistoryAsync(candidate);
        Status = "予定を保存しました。";
        await SyncAfterLocalChangeAsync();
    }

    private async Task DeleteEventWithRecurrenceAsync(RecurrenceEditScope? recurrenceScope)
    {
        if (SelectedEvent is null)
        {
            return;
        }

        if (recurrenceScope is null)
        {
            await _repository.DeleteEventAsync(SelectedEvent);
            SelectedEvent = null;
            await RefreshCalendarAsync();
            Status = "予定を削除しました。";
            await SyncAfterLocalChangeAsync();
            return;
        }

        switch (recurrenceScope.Value)
        {
            case RecurrenceEditScope.ThisOccurrence:
                await DeleteSingleOccurrenceAsync();
                break;
            case RecurrenceEditScope.ThisAndFollowing:
                await DeleteThisAndFollowingAsync();
                break;
            case RecurrenceEditScope.AllEvents:
                await DeleteEntireSeriesAsync();
                break;
        }

        SelectedEvent = null;
        await RefreshCalendarAsync();
        Status = "予定を削除しました。";
        await SyncAfterLocalChangeAsync();
    }

    private async Task SaveEventWithCalendarMoveAsync(CalendarEvent candidate, CalendarEvent? original)
    {
        if (original is not null
            && !string.IsNullOrWhiteSpace(original.GoogleEventId)
            && !string.Equals(original.CalendarId, candidate.CalendarId, StringComparison.Ordinal))
        {
            var tombstone = CloneEventForEditing(original);
            tombstone.Id = Guid.NewGuid().ToString("N");
            tombstone.IsDeleted = true;
            tombstone.IsDirty = true;
            tombstone.LastSyncedAt = null;
            await _repository.SaveEventAsync(tombstone);

            candidate.GoogleEventId = null;
            candidate.LastSyncedAt = null;
            if (candidate.IsRecurrenceException)
            {
                candidate.RecurringEventId = null;
                candidate.RecurringParentId = null;
                candidate.OriginalStart = null;
            }
        }

        await _repository.SaveEventAsync(candidate);
    }

    private CalendarEvent? BuildEditedEventAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Status = "件名を入力してください。";
            return null;
        }

        var calendarEvent = SelectedEvent is null
            ? new CalendarEvent()
            : CloneEventForEditing(SelectedEvent);
        calendarEvent.Title = Title.Trim();
        calendarEvent.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        calendarEvent.Location = string.IsNullOrWhiteSpace(Location) ? null : Location.Trim();
        calendarEvent.CalendarId = ResolveEditorCalendarId();
        calendarEvent.IsAllDay = IsAllDay;
        calendarEvent.ReminderMinutesBeforeStart = ReminderMinutesBeforeStart;
        calendarEvent.ColorId = EditorColorId;
        calendarEvent.IsDirty = true;
        calendarEvent.IsDeleted = false;

        if (IsAllDay)
        {
            calendarEvent.Start = new DateTimeOffset(StartDate.Date);
            calendarEvent.End = new DateTimeOffset(EndDate.Date.AddDays(1));
        }
        else
        {
            if (!TryParseEditorTime(StartTime, out var startTime) || !TryParseEditorTime(EndTime, out var endTime))
            {
                Status = "時刻は HH:mm 形式、または4桁数字(例: 0900, 1234)で入力してください。";
                return null;
            }

            calendarEvent.Start = new DateTimeOffset(StartDate.Date.Add(startTime));
            calendarEvent.End = new DateTimeOffset(EndDate.Date.Add(endTime));
            if (calendarEvent.End <= calendarEvent.Start)
            {
                Status = "終了日時は開始日時より後にしてください。";
                return null;
            }
        }

        return calendarEvent;
    }

    private async Task SaveSingleOccurrenceAsync(CalendarEvent candidate)
    {
        if (SelectedEvent is null)
        {
            return;
        }

        if (!SelectedEvent.IsGeneratedOccurrence && SelectedEvent.IsRecurrenceException)
        {
            candidate.IsRecurrenceException = true;
            candidate.RecurringParentId = SelectedEvent.RecurringParentId;
            candidate.RecurringEventId = SelectedEvent.RecurringEventId;
            candidate.OriginalStart = SelectedEvent.OriginalStart;
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            SelectedEvent = candidate;
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            SelectedEvent = candidate;
            return;
        }

        candidate.Id = SelectedEvent.IsGeneratedOccurrence ? Guid.NewGuid().ToString("N") : candidate.Id;
        candidate.GoogleEventId = SelectedEvent.GoogleEventId;
        candidate.RecurringParentId = master.Id;
        candidate.RecurringEventId = master.GoogleEventId;
        candidate.OriginalStart = SelectedEvent.OriginalStart ?? SelectedEvent.Start;
        candidate.IsRecurrenceException = true;
        candidate.RecurrenceJson = null;
        master.RecurrenceJson = RecurrenceRuleHelper.AddExDate(master.RecurrenceJson, candidate.OriginalStart.Value, master.IsAllDay);
        master.IsDirty = true;
        await _repository.SaveEventAsync(master);
        await _repository.SaveEventAsync(candidate);
        SelectedEvent = candidate;
    }

    private async Task SaveEntireSeriesAsync(CalendarEvent candidate)
    {
        if (SelectedEvent is null)
        {
            await _repository.SaveEventAsync(candidate);
            SelectedEvent = candidate;
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            SelectedEvent = candidate;
            return;
        }

        var target = CloneEventForEditing(master);
        ApplySeriesEditValues(target, candidate, SelectedEvent);
        target.IsDirty = true;
        await SaveEventWithCalendarMoveAsync(target, master);
        SelectedEvent = target;
    }

    private async Task SaveThisAndFollowingAsync(CalendarEvent candidate)
    {
        if (SelectedEvent is null)
        {
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            SelectedEvent = candidate;
            return;
        }

        var splitStart = SelectedEvent.OriginalStart ?? SelectedEvent.Start;
        var original = CloneEventForEditing(master);
        original.RecurrenceJson = RecurrenceRuleHelper.BuildSplitSourceRecurrenceJson(master, splitStart);
        original.IsDirty = true;
        await _repository.SaveEventAsync(original);

        var future = CloneEventForEditing(master);
        future.Id = Guid.NewGuid().ToString("N");
        future.GoogleEventId = null;
        future.RecurringEventId = null;
        future.RecurringParentId = null;
        future.OriginalStart = null;
        future.IsRecurrenceException = false;
        ApplySeriesEditValues(future, candidate, SelectedEvent);
        future.RecurrenceJson = RecurrenceRuleHelper.BuildSplitFutureRecurrenceJson(master, splitStart);
        future.IsDirty = true;
        await _repository.SaveEventAsync(future);

        foreach (var child in await _repository.LoadSeriesEventsAsync(master.Id, master.GoogleEventId))
        {
            if (!child.IsRecurrenceException || child.OriginalStart is null || child.OriginalStart < splitStart)
            {
                continue;
            }

            var moved = CloneEventForEditing(child);
            moved.Id = Guid.NewGuid().ToString("N");
            moved.GoogleEventId = null;
            moved.RecurringParentId = future.Id;
            moved.RecurringEventId = future.GoogleEventId;
            moved.IsDirty = true;
            await _repository.SaveEventAsync(moved);
        }

        SelectedEvent = future;
    }

    private async Task DeleteSingleOccurrenceAsync()
    {
        if (SelectedEvent is null)
        {
            return;
        }

        if (SelectedEvent.IsRecurrenceException && !SelectedEvent.IsGeneratedOccurrence)
        {
            await _repository.DeleteEventAsync(SelectedEvent);
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await _repository.DeleteEventAsync(SelectedEvent);
            return;
        }

        var tombstone = new CalendarEvent
        {
            Title = SelectedEvent.Title,
            Description = SelectedEvent.Description,
            Location = SelectedEvent.Location,
            CalendarId = SelectedEvent.CalendarId,
            Start = SelectedEvent.Start,
            End = SelectedEvent.End,
            IsAllDay = SelectedEvent.IsAllDay,
            ColorId = SelectedEvent.ColorId,
            IsDeleted = true,
            IsDirty = true,
            IsTodoLike = SelectedEvent.IsTodoLike,
            RecurringParentId = master.Id,
            RecurringEventId = master.GoogleEventId,
            OriginalStart = SelectedEvent.OriginalStart ?? SelectedEvent.Start,
            IsRecurrenceException = true
        };
        master.RecurrenceJson = RecurrenceRuleHelper.AddExDate(master.RecurrenceJson, tombstone.OriginalStart.Value, master.IsAllDay);
        master.IsDirty = true;
        await _repository.SaveEventAsync(master);
        await _repository.SaveEventAsync(tombstone);
    }

    private async Task DeleteEntireSeriesAsync()
    {
        if (SelectedEvent is null)
        {
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is not null)
        {
            await _repository.DeleteEventAsync(master);
        }

        foreach (var child in await _repository.LoadSeriesEventsAsync(master?.Id ?? SelectedEvent.RecurringParentId, master?.GoogleEventId ?? SelectedEvent.RecurringEventId))
        {
            await _repository.DeleteEventAsync(child);
        }
    }

    private async Task DeleteThisAndFollowingAsync()
    {
        if (SelectedEvent is null)
        {
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await _repository.DeleteEventAsync(SelectedEvent);
            return;
        }

        var splitStart = SelectedEvent.OriginalStart ?? SelectedEvent.Start;
        master.RecurrenceJson = RecurrenceRuleHelper.BuildSplitSourceRecurrenceJson(master, splitStart);
        master.IsDirty = true;
        await _repository.SaveEventAsync(master);

        foreach (var child in await _repository.LoadSeriesEventsAsync(master.Id, master.GoogleEventId))
        {
            if (child.OriginalStart is not null && child.OriginalStart >= splitStart)
            {
                await _repository.DeleteEventAsync(child);
            }
        }
    }

    private async Task<CalendarEvent?> ResolveSeriesMasterAsync(CalendarEvent selectedEvent)
    {
        if (selectedEvent.IsRecurringMaster)
        {
            return await _repository.FindMasterByIdAsync(selectedEvent.Id);
        }

        if (!string.IsNullOrWhiteSpace(selectedEvent.RecurringParentId))
        {
            return _storedEvents.FirstOrDefault(item => item.Id == selectedEvent.RecurringParentId && item.IsRecurringMaster)
                ?? await _repository.FindMasterByIdAsync(selectedEvent.RecurringParentId);
        }

        if (!string.IsNullOrWhiteSpace(selectedEvent.RecurringEventId))
        {
            return _storedEvents.FirstOrDefault(item => item.GoogleEventId == selectedEvent.RecurringEventId && item.IsRecurringMaster)
                ?? (await _repository.LoadSeriesEventsAsync(null, selectedEvent.RecurringEventId)).FirstOrDefault(item => item.IsRecurringMaster);
        }

        return null;
    }

    private void ApplySeriesEditValues(CalendarEvent target, CalendarEvent candidate, CalendarEvent selectedEvent)
    {
        var dayShift = (candidate.Start.Date - selectedEvent.Start.Date).Days;
        target.Title = candidate.Title;
        target.Description = candidate.Description;
        target.Location = candidate.Location;
        target.CalendarId = candidate.CalendarId;
        target.IsAllDay = candidate.IsAllDay;
        target.ColorId = candidate.ColorId;
        target.Start = dayShift == 0
            ? new DateTimeOffset(target.Start.Date.Add(candidate.Start.TimeOfDay), candidate.Start.Offset)
            : target.Start.AddDays(dayShift).Date.Add(candidate.Start.TimeOfDay);
        target.End = dayShift == 0
            ? new DateTimeOffset(target.End.Date.Add(candidate.End.TimeOfDay), candidate.End.Offset)
            : target.End.AddDays(dayShift).Date.Add(candidate.End.TimeOfDay);

        if (candidate.IsAllDay)
        {
            var durationDays = Math.Max(1, (candidate.End.Date - candidate.Start.Date).Days);
            target.Start = new DateTimeOffset(target.Start.Date);
            target.End = new DateTimeOffset(target.Start.Date.AddDays(durationDays));
        }
        else
        {
            var duration = candidate.End - candidate.Start;
            target.End = target.Start.Add(duration);
        }
    }

    private static CalendarEvent CloneEventForEditing(CalendarEvent source)
    {
        return new CalendarEvent
        {
            Id = source.Id,
            GoogleEventId = source.GoogleEventId,
            RecurringEventId = source.RecurringEventId,
            RecurringParentId = source.RecurringParentId,
            OriginalStart = source.OriginalStart,
            IsRecurrenceException = source.IsRecurrenceException,
            CalendarId = source.CalendarId,
            Title = source.Title,
            Description = source.Description,
            Location = source.Location,
            Start = source.Start,
            End = source.End,
            IsAllDay = source.IsAllDay,
            ColorId = source.ColorId,
            ReminderMinutesBeforeStart = source.ReminderMinutesBeforeStart,
            RecurrenceJson = source.RecurrenceJson,
            IsDeleted = source.IsDeleted,
            UpdatedAt = source.UpdatedAt,
            LastSyncedAt = source.LastSyncedAt,
            IsDirty = source.IsDirty,
            DirtyFields = source.DirtyFields,
            IsTodoLike = source.IsTodoLike,
            DisplayColor = source.DisplayColor,
            DisplayForegroundColor = source.DisplayForegroundColor,
            IsGeneratedOccurrence = source.IsGeneratedOccurrence
        };
    }

    private static CalendarEvent CloneEventAsNewLocalEvent(CalendarEvent source)
    {
        var clone = CloneEventForEditing(source);
        clone.Id = Guid.NewGuid().ToString("N");
        clone.GoogleEventId = null;
        clone.RecurringEventId = null;
        clone.RecurringParentId = null;
        clone.OriginalStart = null;
        clone.IsRecurrenceException = false;
        clone.RecurrenceJson = null;
        clone.IsDeleted = false;
        clone.IsDirty = true;
        clone.LastSyncedAt = null;
        clone.IsGeneratedOccurrence = false;
        return clone;
    }

    public static bool TryParseEditorTime(string? value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 4 && trimmed.All(char.IsDigit))
        {
            var hour = int.Parse(trimmed[..2], CultureInfo.InvariantCulture);
            var minute = int.Parse(trimmed[2..], CultureInfo.InvariantCulture);
            if (hour is >= 0 and <= 23 && minute is >= 0 and <= 59)
            {
                time = new TimeSpan(hour, minute, 0);
                return true;
            }

            return false;
        }

        return TimeSpan.TryParseExact(
            trimmed,
            ["h\\:mm", "hh\\:mm"],
            CultureInfo.InvariantCulture,
            out time)
            && time.Days == 0
            && time.Hours is >= 0 and <= 23
            && time.Minutes is >= 0 and <= 59
            && time.Seconds == 0;
    }

    private async Task BrowseOAuthClientAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Google OAuth client JSON (*.json)|*.json|All files (*.*)|*.*",
            Title = "Desktop OAuth client JSONを選択"
        };

        if (dialog.ShowDialog() == true)
        {
            OAuthClientJsonPath = dialog.FileName;
            _settings.OAuthClientJsonPath = dialog.FileName;
            await _repository.SaveSettingsAsync(_settings);
            await ReloadAvailableCalendarsAsync();
            Status = "OAuth client JSONを保存しました。";
        }
    }

    private async Task AuthorizeAsync()
    {
        await SaveOAuthPathAsync();
        if (string.IsNullOrWhiteSpace(_settings.OAuthClientJsonPath))
        {
            Status = "先にOAuth client JSONを設定してください。";
            return;
        }

        Status = "ブラウザーでGoogle認証を続行してください。";
        await _syncService.AuthorizeAsync(_settings.OAuthClientJsonPath);
        _eventColorPalette = await _syncService.RefreshEventColorPaletteAsync();
        await ReloadAvailableCalendarsAsync();
        await RefreshCalendarAsync();
        Status = "Google認証が完了しました。";
    }

    private void UpdateSegmentSelection()
    {
        var selectedDayIndex = SelectedDay is null ? -1 : CalendarDays.IndexOf(SelectedDay);
        var selectedRow = selectedDayIndex < 0 ? -1 : selectedDayIndex / 7;

        for (var index = 0; index < CalendarDays.Count; index++)
        {
            foreach (var segment in CalendarDays[index].Segments)
            {
                segment.IsSelected = selectedRow >= 0
                    && index / 7 == selectedRow
                    && SameVisibleOccurrence(segment.Event, SelectedEvent);
            }
        }
    }

    private static bool SameVisibleOccurrence(CalendarEvent? left, CalendarEvent? right)
    {
        return left is not null
            && right is not null
            && (ReferenceEquals(left, right)
                || string.Equals(left.Id, right.Id, StringComparison.Ordinal)
                    && left.Start == right.Start
                    && left.OriginalStart == right.OriginalStart);
    }

    private CalendarEvent? FindMovedVisibleEvent(CalendarEvent? selectedAfterSave, CalendarEvent candidate, DateTime targetDate)
    {
        if (selectedAfterSave is not null)
        {
            var selected = _visibleEvents.FirstOrDefault(item => SameVisibleOccurrence(item, selectedAfterSave));
            if (selected is not null)
            {
                return selected;
            }
        }

        return _visibleEvents.FirstOrDefault(item =>
            string.Equals(item.Id, candidate.Id, StringComparison.Ordinal)
            && DateRangeHelper.OccursOn(item, targetDate.Date));
    }

    private async Task SyncAfterLocalChangeAsync()
    {
        if (_settings.SyncAfterLocalChange && CanSynchronize())
        {
            await SynchronizeAsync(reportErrors: false, SyncInvocationKind.LocalChange);
        }
    }

    private bool CanSynchronize()
    {
        return !string.IsNullOrWhiteSpace(_settings.OAuthClientJsonPath)
            && File.Exists(_settings.OAuthClientJsonPath);
    }

    private async Task<SyncResult?> SynchronizeAsync(bool reportErrors, SyncInvocationKind invocationKind)
    {
        if (Interlocked.Exchange(ref _syncInProgress, 1) != 0)
        {
            Interlocked.Exchange(ref _syncRerunRequested, 1);
            return null;
        }

        try
        {
            await SaveOAuthPathAsync();
            if (!CanSynchronize())
            {
                if (reportErrors)
                {
                    Status = "先にOAuth client JSONを設定してください。";
                }

                return null;
            }

            Status = "Googleカレンダーと同期中...";
            IsSynchronizing = true;
            var result = await _syncService.SyncAsync(
                _settings,
                refreshReminderMetadataAfterSync: invocationKind == SyncInvocationKind.Manual);
            var finishedAt = DateTimeOffset.Now;
            if (invocationKind == SyncInvocationKind.Manual)
            {
                _settings.LastManualSyncAt = finishedAt;
            }
            else if (invocationKind == SyncInvocationKind.Automatic)
            {
                _settings.LastAutomaticSyncAt = finishedAt;
            }

            await _repository.SaveSettingsAsync(_settings);
            Status = "カレンダー再読み込み中...";
            var remaining = (await _repository.LoadDirtyEventsAsync()).Count;
            try
            {
                _eventColorPalette = await _syncService.RefreshEventColorPaletteAsync();
                await ReloadAvailableCalendarsAsync();
                await RefreshCalendarAsync();
            }
            catch (Exception reloadEx)
            {
                Debug.WriteLine(reloadEx);
                Status = $"同期は完了しましたが、カレンダー再読み込みに失敗しました: {reloadEx.Message} / 未同期残数 {remaining}";
                return result;
            }

            Status = $"同期が完了しました: {result.Message} / 未同期残数 {remaining}";
            if (result.Failed > 0 || result.Conflicts > 0 || remaining > 0)
            {
                Status += "。Google同期診断を確認してください。";
            }
            return result;
        }
        catch (Exception ex) when (reportErrors)
        {
            Debug.WriteLine(ex);
            await _syncService.RecordFailedSyncAsync(ex.Message, _settings.EnableSyncDiagnostics);
            Status = "同期に失敗しました。Google同期診断を確認してください。";
            throw;
        }
        catch (Exception ex) when (!reportErrors)
        {
            Debug.WriteLine(ex);
            await _syncService.RecordFailedSyncAsync(ex.Message, _settings.EnableSyncDiagnostics);
            Status = $"同期に失敗しました。Google同期診断を確認してください。未同期の変更は保持されています: {ex.Message}";
            return null;
        }
        finally
        {
            Interlocked.Exchange(ref _syncInProgress, 0);
            IsSynchronizing = false;
            await RefreshOperationalStatusAsync(null);
            if (Interlocked.Exchange(ref _syncRerunRequested, 0) != 0)
            {
                await SynchronizeAsync(reportErrors: false, SyncInvocationKind.LocalChange);
            }
        }
    }

    public async Task ClearTokensAsync()
    {
        await _syncService.ClearTokensAsync();
        Status = "保存済みGoogleトークンを削除しました。";
    }

    private enum SyncInvocationKind
    {
        Manual,
        Automatic,
        LocalChange
    }

    private async Task SaveOAuthPathAsync()
    {
        _settings.OAuthClientJsonPath = string.IsNullOrWhiteSpace(OAuthClientJsonPath) ? null : OAuthClientJsonPath.Trim();
        _settings.VisibleCalendarIds = AvailableCalendars.Where(item => item.IsSelected).Select(item => item.Id).ToList();
        _settings.ActiveCalendarId = ResolveEditorCalendarId();
        await _repository.SaveSettingsAsync(_settings);
    }

    private void ApplyFavGCalSchedulerSettings(string sourceFolder)
    {
        var iniPath = Path.Combine(sourceFolder, "FavGCalScheduler.ini");
        if (!File.Exists(iniPath))
        {
            return;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;
        foreach (var line in File.ReadLines(iniPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1].Trim();
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();
            if (section.Equals("DISP_INFO", StringComparison.OrdinalIgnoreCase)
                && new[] { "DeletePopup", "AppClose", "EditScheduleWindowHide", "StartWeekdayIndex", "WeekdayType", "FontSize", "BottomInfoFontSize", "ToDoRunLimitMonthCount", "ToDoCompLimitMonthCount" }
                    .Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                values[key] = value;
            }
            else if (section.Equals("APP_INFO", StringComparison.OrdinalIgnoreCase)
                     && new[] { "CreateScheduleNoHistory", "ScheduleDeaultAllDay", "ScheduleDeaultAlarmIndex" }
                         .Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                values[key] = value;
            }
            else if (section.Equals("SYNC_INFO", StringComparison.OrdinalIgnoreCase)
                     && new[] { "AddEditDelSync", "SyncIntervalMin" }.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                values[key] = value;
            }
        }

        if (values.TryGetValue("DeletePopup", out var deletePopup))
        {
            _settings.ConfirmBeforeDelete = deletePopup != "0";
        }

        if (values.TryGetValue("AppClose", out var appClose))
        {
            _settings.CloseButtonExitsApplication = appClose != "0";
        }

        if (values.TryGetValue("ScheduleDeaultAllDay", out var defaultAllDay))
        {
            _settings.DefaultNewEventIsAllDay = defaultAllDay != "0";
        }

        if (values.TryGetValue("EditScheduleWindowHide", out var editScheduleWindowHide))
        {
            _settings.HideMainWindowWhileEditingSchedule = editScheduleWindowHide != "0";
        }

        if (values.TryGetValue("StartWeekdayIndex", out var startWeekday)
            && int.TryParse(startWeekday, out var startWeekdayIndex))
        {
            _settings.WeekStartsOnMonday = startWeekdayIndex == 1;
        }

        if (values.TryGetValue("WeekdayType", out var weekdayType)
            && int.TryParse(weekdayType, out var weekdayTypeIndex))
        {
            _settings.WeekdayDisplayType = weekdayTypeIndex switch
            {
                1 => WeekdayDisplayType.EnglishFull,
                2 => WeekdayDisplayType.JapaneseShort,
                _ => WeekdayDisplayType.EnglishShort
            };
        }

        if (values.TryGetValue("FontSize", out var fontSize) && int.TryParse(fontSize, out var fontIndex))
        {
            _settings.CalendarLabelFontSizeIndex = Math.Clamp(fontIndex + 1, 1, 3);
        }

        if (values.TryGetValue("BottomInfoFontSize", out var sideFontSize) && int.TryParse(sideFontSize, out var sideFontIndex))
        {
            _settings.SideListFontSizeIndex = Math.Clamp(sideFontIndex + 1, 1, 3);
        }

        if (values.TryGetValue("ToDoRunLimitMonthCount", out var runningLimit) && int.TryParse(runningLimit, out var runningMonths))
        {
            _settings.IncompleteTodoDisplayPeriodMonths = AppSettingsNormalizer.NormalizeTodoMonths(runningMonths);
        }

        if (values.TryGetValue("ToDoCompLimitMonthCount", out var completedLimit) && int.TryParse(completedLimit, out var completedMonths))
        {
            _settings.CompletedTodoDisplayPeriodMonths = AppSettingsNormalizer.NormalizeTodoMonths(completedMonths);
        }

        if (values.TryGetValue("CreateScheduleNoHistory", out var noHistory))
        {
            _settings.ReuseLastScheduleInput = noHistory == "0";
        }

        if (values.TryGetValue("ScheduleDeaultAlarmIndex", out var alarmIndex) && int.TryParse(alarmIndex, out var alarm))
        {
            _settings.DefaultScheduleReminderMinutes = alarm switch
            {
                1 => 0,
                2 => 5,
                3 => 10,
                4 => 30,
                5 => 60,
                _ => null
            };
        }

        if (values.TryGetValue("AddEditDelSync", out var syncAfterLocalChange))
        {
            _settings.SyncAfterLocalChange = syncAfterLocalChange != "0";
        }

        if (values.TryGetValue("SyncIntervalMin", out var syncMinutes) && int.TryParse(syncMinutes, out var interval))
        {
            _settings.AutomaticSyncIntervalMinutes = new[] { 30, 60, 120, 360 }.Contains(interval) ? interval : null;
        }
    }

    private async Task<IReadOnlyList<GoogleCalendarSelectionItem>> LoadAvailableCalendarsCoreAsync()
    {
        IReadOnlyList<GoogleCalendarInfo> calendars;
        if (!string.IsNullOrWhiteSpace(_settings.OAuthClientJsonPath) && File.Exists(_settings.OAuthClientJsonPath))
        {
            try
            {
                calendars = await _syncService.ListCalendarsAsync(_settings.OAuthClientJsonPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Status = "Googleカレンダー一覧を取得できませんでした。OAuth設定またはネットワークを確認してください。";
                calendars = [];
            }
        }
        else
        {
            calendars = [];
        }

        var selectedIds = _settings.VisibleCalendarIds.Count == 0
            ? [string.IsNullOrWhiteSpace(_settings.ActiveCalendarId) ? GoogleCalendarDefaults.PrimaryCalendarId : _settings.ActiveCalendarId]
            : _settings.VisibleCalendarIds;

        if (calendars.Count == 0)
        {
            calendars = selectedIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Select(id => new GoogleCalendarInfo(id, id))
                .ToList();
        }

        if (calendars.Count == 0)
        {
            calendars = [new GoogleCalendarInfo(GoogleCalendarDefaults.PrimaryCalendarId, "primary")];
        }

        return calendars
            .Select(calendar => new GoogleCalendarSelectionItem
            {
                Id = calendar.Id,
                Summary = calendar.Summary,
                IsSelected = selectedIds.Contains(calendar.Id, StringComparer.Ordinal)
            })
            .ToArray();
    }

    private void RefreshCalendarNames()
    {
        CalendarNames.Clear();
        foreach (var calendar in AvailableCalendars)
        {
            CalendarNames.Add(calendar.Summary);
        }
    }

    private string ResolveEditorCalendarId()
    {
        if (AvailableCalendars.Any(item => item.Id == EditorCalendarId))
        {
            return EditorCalendarId;
        }

        if (AvailableCalendars.Any(item => item.IsSelected))
        {
            return AvailableCalendars.First(item => item.IsSelected).Id;
        }

        if (AvailableCalendars.Count > 0)
        {
            return AvailableCalendars[0].Id;
        }

        return string.IsNullOrWhiteSpace(_settings.ActiveCalendarId) ? GoogleCalendarDefaults.PrimaryCalendarId : _settings.ActiveCalendarId;
    }

    private static IReadOnlyList<string> DeserializeHistory(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    private static IReadOnlyList<string> AddHistoryValue(IReadOnlyList<string> history, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return history;
        }

        return new[] { value.Trim() }
            .Concat(history.Where(item => !string.Equals(item, value.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Take(50)
            .ToArray();
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

internal sealed record CalendarCacheKey(DateTime Month, bool WeekStartsOnMonday, string VisibleCalendarIds);

internal sealed record CalendarSnapshotBuildContext(
    bool WeekStartsOnMonday,
    IReadOnlySet<string> VisibleCalendarIds,
    IReadOnlyDictionary<string, string> CalendarNames,
    IReadOnlyDictionary<string, EventDisplayColors> EventColorPalette);

internal sealed record CalendarRefreshSnapshot(
    DateTime Month,
    DateTime GridStart,
    DateTime GridEnd,
    IReadOnlyList<CalendarEvent> StoredEvents,
    IReadOnlyList<CalendarEvent> DayDirectiveEvents,
    IReadOnlyList<CalendarEvent> VisibleEvents);
