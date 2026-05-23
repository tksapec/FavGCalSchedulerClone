using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Win32;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly CalendarRepository _repository;
    private readonly GoogleCalendarSyncService _syncService;
    private readonly BackupService _backupService = new();
    private readonly CalendarCsvService _csvService = new();
    private readonly FavGCalSchedulerImportService _favGCalImportService;
    private IReadOnlyList<CalendarEvent> _storedEvents = [];
    private IReadOnlyList<CalendarEvent> _visibleEvents = [];
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
    private DateTime? _pendingSelectedDate;
    private CalendarViewMode _currentViewMode = CalendarViewMode.Month;
    private string _editorCalendarId = GoogleCalendarDefaults.PrimaryCalendarId;
    private int _refreshGeneration;

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
    public string CalendarStatusText => FormatCalendarStatus(DateTime.Today);
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

                RefreshSelectedDayEvents();
                RefreshSevenDayEvents();
                OnPropertyChanged(nameof(CurrentPeriodTitle));
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

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, NormalizeTabIndex(value));
    }

    public int StartupTabIndex => _settings.StartupTabIndex;
    public bool ConfirmBeforeDelete => _settings.ConfirmBeforeDelete;
    public bool CloseButtonExitsApplication => _settings.CloseButtonExitsApplication;
    public bool DefaultNewEventIsAllDay => _settings.DefaultNewEventIsAllDay;
    public bool UseWindowsToastNotifications => _settings.UseWindowsToastNotifications;
    public string DefaultBackupFileName => $"FavGCalSchedulerClone-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip";

    public async Task InitializeAsync()
    {
        await _repository.InitializeAsync();
        _settings = NormalizeSettings(await _repository.LoadSettingsAsync());
        OAuthClientJsonPath = _settings.OAuthClientJsonPath ?? "";
        SelectedTabIndex = _settings.StartupTabIndex;
        SelectedDay = null;
        await ReloadTagsAsync();
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

    public void BeginNewEvent(DateTime date)
    {
        SelectedEvent = null;
        Title = "";
        Description = "";
        Location = "";
        StartDate = date.Date;
        EndDate = date.Date;
        StartTime = "09:00";
        EndTime = "10:00";
        IsAllDay = _settings.DefaultNewEventIsAllDay;
        ReminderMinutesBeforeStart = null;
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
            ReminderMinutesBeforeStart = ReminderMinutesBeforeStart
        };

        await _repository.SaveEventAsync(todoEvent);
        await RefreshCalendarAsync();
        Status = "ToDoを保存しました。同期するとGoogleカレンダーへ反映されます。";
    }

    public async Task SaveTodoAsync(CalendarEvent editingTodo, DateTime dueDate, string priority, int progress, string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            Status = "Title is required.";
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
        editingTodo.ReminderMinutesBeforeStart = ReminderMinutesBeforeStart;

        await _repository.SaveEventAsync(editingTodo);
        await RefreshCalendarAsync();
        SelectedEvent = _visibleEvents.FirstOrDefault(item => item.Id == editingTodo.Id) ?? editingTodo;
        Status = "ToDo saved.";
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
        var (gridStart, gridEnd) = DateRangeHelper.MonthGridRange(CurrentMonth);
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
        await _repository.SaveSettingsAsync(_settings);

        OnPropertyChanged(nameof(StartupTabIndex));
        OnPropertyChanged(nameof(ConfirmBeforeDelete));
        OnPropertyChanged(nameof(CloseButtonExitsApplication));
        OnPropertyChanged(nameof(DefaultNewEventIsAllDay));
        OnPropertyChanged(nameof(UseWindowsToastNotifications));
        Status = "アプリ設定を保存しました。";
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
            await _repository.SaveSettingsAsync(_settings);
            OnPropertyChanged(nameof(ConfirmBeforeDelete));
            OnPropertyChanged(nameof(CloseButtonExitsApplication));
            OnPropertyChanged(nameof(DefaultNewEventIsAllDay));
        }

        await ReloadAvailableCalendarsAsync();
        await RefreshCalendarAsync();
        Status = $"FavGCalSchedulerデータを取り込みました: 追加 {result.ImportedCount} 件、既存紐付け {result.LinkedExistingGoogleCount} 件、重複スキップ {result.SkippedDuplicateCount} 件";
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

        var (gridStart, gridEnd) = DateRangeHelper.MonthGridRange(CurrentMonth);
        var storedEvents = await _repository.LoadEventsAsync(new DateTimeOffset(gridStart), new DateTimeOffset(gridEnd), includeDeleted: true);
        var visibleEvents = RecurrenceExpansionService
            .ExpandForRange(storedEvents, new DateTimeOffset(gridStart), new DateTimeOffset(gridEnd))
            .Where(IsVisible)
            .ToArray();
        if (generation != _refreshGeneration)
        {
            return;
        }

        _storedEvents = storedEvents;
        _visibleEvents = visibleEvents;
        ApplyDisplayColors(_visibleEvents);

        CalendarDays.Clear();
        for (var date = gridStart; date < gridEnd; date = date.AddDays(1))
        {
            var day = new CalendarDay
            {
                Date = date,
                IsCurrentMonth = date.Month == CurrentMonth.Month,
                IsWorkdayOverride = TagService.HasWorkdayOverride(_visibleEvents, date),
                IsHoliday = TagService.HasHolidayWithoutWorkdayOverride(_visibleEvents, date)
            };

            foreach (var calendarEvent in _visibleEvents.Where(e => DateRangeHelper.OccursOn(e, date)).Take(5))
            {
                day.Events.Add(calendarEvent);
            }

            CalendarDays.Add(day);
        }

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
        RefreshSelectedDayEvents();
        RefreshSevenDayEvents();
        RefreshTodos(_visibleEvents);
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

    private void RefreshTodos(IEnumerable<CalendarEvent> events)
    {
        TodoEvents.Clear();
        CompletedTodoEvents.Clear();
        foreach (var item in events.Where(e => e.IsTodoLike && !e.IsDeleted).OrderBy(e => e.Start).ThenBy(e => e.TodoPriority).Take(100))
        {
            if (item.IsTodoDone)
            {
                CompletedTodoEvents.Add(item);
            }
            else
            {
                TodoEvents.Add(item);
            }
        }
    }

    private void ApplyDisplayColors(IEnumerable<CalendarEvent> events)
    {
        foreach (var calendarEvent in events)
        {
            calendarEvent.DisplayColor = TagService.FindDisplayTag(calendarEvent, Tags)?.Color ?? "#FFFFFF";
        }
    }

    private bool IsVisible(CalendarEvent calendarEvent)
    {
        var displayTag = TagService.FindDisplayTag(calendarEvent, Tags);
        var calendarVisible = AvailableCalendars.Count == 0
            || AvailableCalendars.Any(item => item.IsSelected && item.Id == calendarEvent.CalendarId);
        return calendarVisible && (displayTag?.IsVisible ?? true);
    }

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
        var start = anchor.Date.AddDays(-(int)anchor.DayOfWeek);
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
            ?? CreateCalendarDay(date, _visibleEvents);
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
        var weekOfYear = elapsedDays / 7;
        var dayOfWeek = date.ToString("dddd", new CultureInfo("ja-JP"));
        return $"{date:yyyy}年({FormatJapaneseEra(date)}){date:MM月dd日} 第{weekOfMonth}{dayOfWeek} {weekOfYear}週目 経過日数 {elapsedDays}日";
    }

    private static string FormatWeekTitle(DateTime date)
    {
        var start = date.Date.AddDays(-(int)date.DayOfWeek);
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
            SelectedEvent = candidate;
            await RefreshCalendarAsync();
            Status = "予定を保存しました。";
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
        Status = "予定を保存しました。";
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
        await ReloadAvailableCalendarsAsync();
        Status = "Google認証が完了しました。";
    }

    private async Task SyncAsync()
    {
        await SaveOAuthPathAsync();
        Status = "Googleカレンダーと同期中...";
        var result = await _syncService.SyncAsync(_settings);
        await ReloadAvailableCalendarsAsync();
        await RefreshCalendarAsync();
        Status = $"同期が完了しました: 送信 {result.Pushed} 件、取得 {result.Pulled} 件。";
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

        var values = File.ReadLines(iniPath)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

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

    private static int NormalizeTabIndex(int tabIndex) => Math.Clamp(tabIndex, 0, 4);
}
