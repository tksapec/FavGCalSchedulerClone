using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
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
    private readonly BackupService _backupService = new();
    private readonly CalendarCsvService _csvService = new();
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
    private IReadOnlyDictionary<string, EventDisplayColors> _eventColorPalette = TagService.DefaultEventColorPalette;
    private IReadOnlyList<string> _scheduleTitleHistory = [];
    private IReadOnlyList<string> _scheduleLocationHistory = [];
    private int _syncInProgress;

    public MainViewModel(CalendarRepository repository, GoogleCalendarSyncService syncService)
    {
        _repository = repository;
        _syncService = syncService;
        _favGCalImportService = new FavGCalSchedulerImportService(repository);

        PreviousMonthCommand = new RelayCommand(() => NavigatePrimary(-1));
        NextMonthCommand = new RelayCommand(() => NavigatePrimary(1));
        PreviousYearCommand = new RelayCommand(() => NavigateSecondary(-1));
        NextYearCommand = new RelayCommand(() => NavigateSecondary(1));
        TodayCommand = new AsyncRelayCommand(GoToTodayAsync);
        ShowMonthViewCommand = new RelayCommand(() => CurrentViewMode = CalendarViewMode.Month);
        ShowWeekViewCommand = new RelayCommand(() => CurrentViewMode = CalendarViewMode.Week);
        ShowDayViewCommand = new RelayCommand(() => CurrentViewMode = CalendarViewMode.Day);
        NewEventCommand = new RelayCommand(NewEvent);
        SaveEventCommand = new AsyncRelayCommand(() => SaveEventWithRecurrenceAsync(null));
        DeleteEventCommand = new AsyncRelayCommand(() => DeleteEventWithRecurrenceAsync(null), () => SelectedEvent is not null);
        MarkSelectedTodoDoneCommand = new AsyncRelayCommand(MarkSelectedTodoDoneAsync, () => SelectedEvent?.IsTodoLike == true && !SelectedEvent.IsTodoDone);
        SyncCommand = new AsyncRelayCommand(SyncAsync);
        ReloadCalendarListCommand = new AsyncRelayCommand(ReloadAvailableCalendarsAsync);
        BrowseOAuthClientCommand = new AsyncRelayCommand(BrowseOAuthClientAsync);
        AuthorizeCommand = new AsyncRelayCommand(AuthorizeAsync);
        ClearTokensCommand = new AsyncRelayCommand(ClearTokensAsync);
        SaveTagsCommand = new AsyncRelayCommand(SaveTagsAsync);
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
    public AsyncRelayCommand ReloadCalendarListCommand { get; }
    public AsyncRelayCommand BrowseOAuthClientCommand { get; }
    public AsyncRelayCommand AuthorizeCommand { get; }
    public AsyncRelayCommand ClearTokensCommand { get; }
    public AsyncRelayCommand SaveTagsCommand { get; }

    public string MonthTitle => CurrentMonth.ToString("yyyy/MM", CultureInfo.InvariantCulture);
    public string JapaneseMonthTitle => $"{CurrentMonth:yyyy}年（{FormatJapaneseEra(CurrentMonth)}） {CurrentMonth.Month}月";
    public string CurrentPeriodTitle => CurrentViewMode switch
    {
        CalendarViewMode.Month => JapaneseMonthTitle,
        CalendarViewMode.Week => FormatWeekTitle(SelectedDay?.Date ?? DateTime.Today),
        CalendarViewMode.Day => FormatDayTitle(SelectedDay?.Date ?? DateTime.Today),
        _ => JapaneseMonthTitle
    };
    public string CalendarStatusText => FormatCalendarStatus(SelectedDay?.Date ?? DateTime.Today);
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
                StartRefreshCalendar();
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
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

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
            .ToArray();

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, NormalizeTabIndex(value));
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
    public IReadOnlyList<string> WeekdayHeaders => CreateWeekdayHeaders();
    public IReadOnlyList<string> ScheduleTitleHistory => _scheduleTitleHistory;
    public IReadOnlyList<string> ScheduleLocationHistory => _scheduleLocationHistory;
    public bool EnableReminderSound => _settings.EnableReminderSound;
    public string? ReminderSoundFilePath => _settings.ReminderSoundFilePath;
    public int ReminderSoundVolume => _settings.ReminderSoundVolume;
    public bool UseWindowsToastNotifications => _settings.UseWindowsToastNotifications;
    public string DefaultBackupFileName => $"FavGCalSchedulerClone-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip";

    public async Task InitializeAsync()
    {
        await _repository.InitializeAsync();
        _settings = NormalizeSettings(await _repository.LoadSettingsAsync());
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
        Status = "準備完了";
    }

    public void NewEvent()
    {
        BeginNewEvent(SelectedDay?.Date ?? DateTime.Today);
    }

    public async Task GoToTodayAsync()
    {
        _pendingSelectedDate = DateTime.Today;
        SetCurrentMonthWithoutRefreshing(DateTime.Today);
        await RefreshCalendarAsync();
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
        SetCurrentMonthWithoutRefreshing(targetDate.Date);
        await RefreshCalendarAsync();
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
            await _repository.SaveEventAsync(candidate);
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

        await _repository.SaveEventAsync(editingTodo);
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

    public async Task<IReadOnlyList<CalendarEvent>> LoadYearEventsAsync(DateTime yearInView)
    {
        var start = new DateTime(yearInView.Year, 1, 1);
        var end = start.AddYears(1);
        var events = await _repository.LoadEventsAsync(new DateTimeOffset(start), new DateTimeOffset(end));
        var expanded = RecurrenceExpansionService.ExpandForRange(events, new DateTimeOffset(start), new DateTimeOffset(end));
        ApplyDisplayColors(expanded);
        return expanded.Where(IsVisible).OrderBy(e => e.Start).ThenBy(e => e.Title).ToArray();
    }

    public async Task<MonthlyPrintPlan> CreateMonthlyPrintPlanAsync()
    {
        var (gridStart, gridEnd) = DateRangeHelper.MonthGridRange(CurrentMonth, _settings.WeekStartsOnMonday);
        var events = await _repository.LoadEventsAsync(new DateTimeOffset(gridStart), new DateTimeOffset(gridEnd));
        var expanded = RecurrenceExpansionService.ExpandForRange(events, new DateTimeOffset(gridStart), new DateTimeOffset(gridEnd));
        ApplyDisplayColors(expanded);
        return MonthlyPrintPlanner.Create(CurrentMonth, expanded.Where(IsVisible));
    }

    public async Task<IReadOnlyList<CalendarEvent>> SearchYearEventsAsync(DateTime yearInView, string query)
    {
        var events = await LoadYearEventsAsync(yearInView);
        if (string.IsNullOrWhiteSpace(query))
        {
            return events;
        }

        return events
            .Where(e => e.SearchText.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
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
        bool defaultNewEventIsAllDay,
        bool useWindowsToastNotifications)
    {
        _settings.StartupTabIndex = NormalizeTabIndex(startupTabIndex);
        _settings.ConfirmBeforeDelete = confirmBeforeDelete;
        _settings.CloseButtonExitsApplication = closeButtonExitsApplication;
        _settings.DefaultNewEventIsAllDay = defaultNewEventIsAllDay;
        _settings.UseWindowsToastNotifications = useWindowsToastNotifications;
        await SaveApplicationSettingsAsync(_settings);
    }

    public AppSettings CreateSettingsSnapshot()
    {
        return JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_settings)) ?? new AppSettings();
    }

    public async Task SaveApplicationSettingsAsync(AppSettings settings)
    {
        _settings = NormalizeSettings(settings);
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
            nameof(ReminderSoundVolume), nameof(UseWindowsToastNotifications)
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
        return await SynchronizeAsync(reportErrors: true);
    }

    public async Task<SyncDiagnosticsSnapshot> LoadSyncDiagnosticsAsync()
    {
        await SaveOAuthPathAsync();
        return await _syncService.LoadDiagnosticsAsync(_settings);
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

        await SynchronizeAsync(reportErrors: false);
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
        await _repository.SaveSettingsAsync(_settings);
        await RefreshCalendarAsync();
    }

    private void StartRefreshCalendar()
    {
        _ = RefreshCalendarSafelyAsync();
    }

    private async Task RefreshCalendarSafelyAsync()
    {
        try
        {
            await RefreshCalendarAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            Status = "カレンダー表示の更新に失敗しました。";
        }
    }

    private async Task RefreshCalendarAsync()
    {
        var generation = Interlocked.Increment(ref _refreshGeneration);
        _settings.DisplayMonth = CurrentMonth;
        await _repository.SaveSettingsAsync(_settings);

        var (gridStart, gridEnd) = DateRangeHelper.MonthGridRange(CurrentMonth, _settings.WeekStartsOnMonday);
        var storedEvents = await _repository.LoadEventsAsync(new DateTimeOffset(gridStart), new DateTimeOffset(gridEnd), includeDeleted: true);
        var calendarEvents = RecurrenceExpansionService
            .ExpandForRange(storedEvents, new DateTimeOffset(gridStart), new DateTimeOffset(gridEnd))
            .Where(IsInVisibleCalendar)
            .ToArray();
        if (generation != _refreshGeneration)
        {
            return;
        }

        _storedEvents = storedEvents;
        _dayDirectiveEvents = calendarEvents;
        _visibleEvents = calendarEvents.Where(IsVisible).ToArray();
        ApplyDisplayColors(_visibleEvents);

        CalendarDays.Clear();
        for (var date = gridStart; date < gridEnd; date = date.AddDays(1))
        {
            CalendarDays.Add(CreateCalendarDay(date, _dayDirectiveEvents));
        }
        CalendarSegmentLayoutService.PopulateSegments(CalendarDays, _visibleEvents);

        if (_pendingSelectedDate is { } pendingSelectedDate)
        {
            SelectedDay = CalendarDays.FirstOrDefault(d => d.Date == pendingSelectedDate.Date) ?? CalendarDays.FirstOrDefault();
            _pendingSelectedDate = null;
        }
        else
        {
            SelectedDay ??= CalendarDays.FirstOrDefault(d => d.Date == DateTime.Today) ?? CalendarDays.FirstOrDefault();
        }

        RefreshVisibleCalendarDays();
        UpdateSegmentSelection();
        RefreshSelectedDayEvents();
        RefreshSevenDayEvents();
        await RefreshTodosAsync();
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
                     .Where(item => !item.IsTodoDone && IsWithinTodoDisplayPeriod(item, _settings.IncompleteTodoDisplayPeriodMonths))
                     .OrderBy(item => item.Start)
                     .ThenBy(item => item.TodoPriority)
                     .Take(100))
        {
            TodoEvents.Add(item);
        }

        foreach (var item in events
                     .Where(item => item.IsTodoDone && IsWithinTodoDisplayPeriod(item, _settings.CompletedTodoDisplayPeriodMonths))
                     .OrderBy(item => item.Start)
                     .ThenBy(item => item.TodoPriority)
                     .Take(100))
        {
            CompletedTodoEvents.Add(item);
        }
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

    private static bool IsWithinTodoDisplayPeriod(CalendarEvent calendarEvent, int months)
    {
        return months == 0 || calendarEvent.Start.Date >= DateTime.Today.AddMonths(-months);
    }

    private void ApplyDisplayColors(IEnumerable<CalendarEvent> events)
    {
        var calendarNames = AvailableCalendars.ToDictionary(item => item.Id, item => item.Summary, StringComparer.Ordinal);
        foreach (var calendarEvent in events)
        {
            var colors = TagService.ResolveDisplayColors(calendarEvent, _eventColorPalette);
            calendarEvent.DisplayColor = colors.Background;
            calendarEvent.DisplayForegroundColor = colors.Foreground;
            calendarEvent.ToolTipText = CalendarEventToolTipFormatter.Format(
                calendarEvent,
                calendarNames.GetValueOrDefault(calendarEvent.CalendarId));
        }
    }

    private bool IsVisible(CalendarEvent calendarEvent)
    {
        var displayTag = TagService.FindDisplayTag(calendarEvent, Tags);
        return IsInVisibleCalendar(calendarEvent)
            && !TagService.IsDayCellDirective(calendarEvent)
            && (displayTag?.IsVisible ?? true);
    }

    private bool IsInVisibleCalendar(CalendarEvent calendarEvent) =>
        AvailableCalendars.Count == 0
        || AvailableCalendars.Any(item => item.IsSelected && item.Id == calendarEvent.CalendarId);

    private void NavigatePrimary(int direction)
    {
        var anchor = SelectedDay?.Date ?? CurrentMonth;
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
        var anchor = SelectedDay?.Date ?? CurrentMonth;
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
        SetCurrentMonthWithoutRefreshing(targetDate.Date);
        StartRefreshCalendar();
    }

    private void RefreshVisibleCalendarDays()
    {
        VisibleCalendarDays.Clear();

        IEnumerable<CalendarDay> days = CurrentViewMode switch
        {
            CalendarViewMode.Month => CalendarDays,
            CalendarViewMode.Week => GetWeekDays(),
            CalendarViewMode.Day => GetDayDays(),
            _ => CalendarDays
        };

        foreach (var day in days)
        {
            VisibleCalendarDays.Add(day);
        }
    }

    private IEnumerable<CalendarDay> GetWeekDays()
    {
        if (CalendarDays.Count == 0)
        {
            return [];
        }

        var anchor = SelectedDay?.Date ?? CurrentMonth;
        var offset = _settings.WeekStartsOnMonday
            ? ((int)anchor.DayOfWeek + 6) % 7
            : (int)anchor.DayOfWeek;
        var start = anchor.Date.AddDays(-offset);
        return Enumerable.Range(0, 7).Select(offset => FindOrCreateCalendarDay(start.AddDays(offset)));
    }

    private IEnumerable<CalendarDay> GetDayDays()
    {
        if (CalendarDays.Count == 0)
        {
            return [];
        }

        return [FindOrCreateCalendarDay((SelectedDay?.Date ?? CurrentMonth).Date)];
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

    private static string FormatJapaneseEra(DateTime date)
    {
        var culture = new CultureInfo("ja-JP", false);
        culture.DateTimeFormat.Calendar = new JapaneseCalendar();
        var eraName = culture.DateTimeFormat.GetEraName(culture.DateTimeFormat.Calendar.GetEra(date));
        var year = culture.DateTimeFormat.Calendar.GetYear(date);
        return $"{eraName}{year}年";
    }

    private static string FormatCalendarStatus(DateTime date)
    {
        var weekOfMonth = ((date.Day - 1) / 7) + 1;
        var elapsedDays = date.DayOfYear;
        var weekOfYear = ((elapsedDays - 1) / 7) + 1;
        var dayOfWeek = date.ToString("dddd", new CultureInfo("ja-JP"));
        return $"{date:yyyy}年({FormatJapaneseEra(date)}){date:MM月dd日} 第{weekOfMonth}{dayOfWeek} {weekOfYear}週目 経過日数 {elapsedDays}日";
    }

    private string FormatWeekTitle(DateTime date)
    {
        var offset = _settings.WeekStartsOnMonday
            ? ((int)date.DayOfWeek + 6) % 7
            : (int)date.DayOfWeek;
        var start = date.Date.AddDays(-offset);
        var end = start.AddDays(6);
        return $"{start:yyyy/M/d} - {end:yyyy/M/d}";
    }

    private static string FormatDayTitle(DateTime date)
    {
        return date.ToString("yyyy/M/d (ddd)", new CultureInfo("ja-JP"));
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
            await _repository.SaveEventAsync(candidate);
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
            if (!TimeSpan.TryParse(StartTime, out var startTime) || !TimeSpan.TryParse(EndTime, out var endTime))
            {
                Status = "時刻は HH:mm 形式で入力してください。";
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
            await _repository.SaveEventAsync(candidate);
            SelectedEvent = candidate;
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await _repository.SaveEventAsync(candidate);
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
            await _repository.SaveEventAsync(candidate);
            SelectedEvent = candidate;
            return;
        }

        var target = CloneEventForEditing(master);
        ApplySeriesEditValues(target, candidate, SelectedEvent);
        target.IsDirty = true;
        await _repository.SaveEventAsync(target);
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
            await _repository.SaveEventAsync(candidate);
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
            IsTodoLike = source.IsTodoLike,
            DisplayColor = source.DisplayColor,
            DisplayForegroundColor = source.DisplayForegroundColor,
            IsGeneratedOccurrence = source.IsGeneratedOccurrence
        };
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

    private async Task SyncAsync()
    {
        await SynchronizeManuallyAsync();
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
            await SynchronizeAsync(reportErrors: false);
        }
    }

    private bool CanSynchronize()
    {
        return !string.IsNullOrWhiteSpace(_settings.OAuthClientJsonPath)
            && File.Exists(_settings.OAuthClientJsonPath);
    }

    private async Task<SyncResult?> SynchronizeAsync(bool reportErrors)
    {
        if (Interlocked.Exchange(ref _syncInProgress, 1) != 0)
        {
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
            var result = await _syncService.SyncAsync(_settings);
            _settings.LastAutomaticSyncAt = DateTimeOffset.Now;
            await _repository.SaveSettingsAsync(_settings);
            _eventColorPalette = await _syncService.RefreshEventColorPaletteAsync();
            await ReloadAvailableCalendarsAsync();
            await RefreshCalendarAsync();
            Status = $"同期が完了しました: 送信 {result.Pushed} 件、取得 {result.Pulled} 件。";
            return result;
        }
        catch (Exception ex) when (reportErrors)
        {
            Debug.WriteLine(ex);
            await _syncService.RecordFailedSyncAsync(ex.Message, _settings.EnableSyncDiagnostics);
            throw;
        }
        catch (Exception ex) when (!reportErrors)
        {
            Debug.WriteLine(ex);
            await _syncService.RecordFailedSyncAsync(ex.Message, _settings.EnableSyncDiagnostics);
            Status = $"自動同期に失敗しました。未同期の変更は保持されています: {ex.Message}";
            return null;
        }
        finally
        {
            Interlocked.Exchange(ref _syncInProgress, 0);
        }
    }

    private async Task ClearTokensAsync()
    {
        await _syncService.ClearTokensAsync();
        Status = "保存済みGoogleトークンを削除しました。";
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
            _settings.IncompleteTodoDisplayPeriodMonths = NormalizeTodoMonths(runningMonths);
        }

        if (values.TryGetValue("ToDoCompLimitMonthCount", out var completedLimit) && int.TryParse(completedLimit, out var completedMonths))
        {
            _settings.CompletedTodoDisplayPeriodMonths = NormalizeTodoMonths(completedMonths);
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

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        settings.StartupTabIndex = NormalizeTabIndex(settings.StartupTabIndex);
        settings.StartupTodoTabIndex = Math.Clamp(settings.StartupTodoTabIndex, 0, 1);
        settings.CalendarLabelFontSizeIndex = Math.Clamp(settings.CalendarLabelFontSizeIndex, 1, 3);
        settings.SideListFontSizeIndex = Math.Clamp(settings.SideListFontSizeIndex, 1, 3);
        settings.WindowOpacity = Math.Clamp(settings.WindowOpacity, 64, 255);
        settings.ReminderSoundVolume = Math.Clamp(settings.ReminderSoundVolume, 0, 100);
        settings.IncompleteTodoDisplayPeriodMonths = NormalizeTodoMonths(settings.IncompleteTodoDisplayPeriodMonths);
        settings.CompletedTodoDisplayPeriodMonths = NormalizeTodoMonths(settings.CompletedTodoDisplayPeriodMonths);
        settings.AutomaticSyncIntervalMinutes = settings.AutomaticSyncIntervalMinutes is int interval
            && new[] { 30, 60, 120, 360 }.Contains(interval)
                ? interval
                : null;
        settings.VisibleCalendarIds = settings.VisibleCalendarIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (settings.VisibleCalendarIds.Count == 0)
        {
            settings.VisibleCalendarIds.Add(string.IsNullOrWhiteSpace(settings.ActiveCalendarId) ? GoogleCalendarDefaults.PrimaryCalendarId : settings.ActiveCalendarId);
        }

        settings.ActiveCalendarId = string.IsNullOrWhiteSpace(settings.ActiveCalendarId)
            ? settings.VisibleCalendarIds[0]
            : settings.ActiveCalendarId;
        return settings;
    }

    private IReadOnlyList<string> CreateWeekdayHeaders()
    {
        var headers = _settings.WeekdayDisplayType switch
        {
            WeekdayDisplayType.EnglishFull => new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
            WeekdayDisplayType.JapaneseShort => new[] { "日", "月", "火", "水", "木", "金", "土" },
            _ => new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }
        };

        return _settings.WeekStartsOnMonday
            ? headers.Skip(1).Concat(headers.Take(1)).ToArray()
            : headers;
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

    private static int NormalizeTodoMonths(int months)
    {
        return new[] { 0, 1, 3, 6, 12 }.Contains(months) ? months : 0;
    }

    private static int NormalizeTabIndex(int tabIndex) => Math.Clamp(tabIndex, 0, 4);
}

public sealed record EventColorSelectionItem(string? Id, string Label, string Background, string Foreground);
