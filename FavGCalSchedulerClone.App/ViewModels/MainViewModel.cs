using System.Collections.ObjectModel;
using System.Globalization;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Win32;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly CalendarRepository _repository;
    private readonly GoogleCalendarSyncService _syncService;
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
    private string _oauthClientJsonPath = "";

    public MainViewModel(CalendarRepository repository, GoogleCalendarSyncService syncService)
    {
        _repository = repository;
        _syncService = syncService;

        PreviousMonthCommand = new RelayCommand(() => ChangeMonth(-1));
        NextMonthCommand = new RelayCommand(() => ChangeMonth(1));
        PreviousYearCommand = new RelayCommand(() => ChangeMonth(-12));
        NextYearCommand = new RelayCommand(() => ChangeMonth(12));
        TodayCommand = new RelayCommand(() => CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        NewEventCommand = new RelayCommand(NewEvent);
        SaveEventCommand = new AsyncRelayCommand(SaveEventAsync);
        DeleteEventCommand = new AsyncRelayCommand(DeleteEventAsync, () => SelectedEvent is not null);
        MarkSelectedTodoDoneCommand = new AsyncRelayCommand(MarkSelectedTodoDoneAsync, () => SelectedEvent?.IsTodoLike == true && !SelectedEvent.IsTodoDone);
        SyncCommand = new AsyncRelayCommand(SyncAsync);
        BrowseOAuthClientCommand = new AsyncRelayCommand(BrowseOAuthClientAsync);
        AuthorizeCommand = new AsyncRelayCommand(AuthorizeAsync);
        ClearTokensCommand = new AsyncRelayCommand(ClearTokensAsync);
        SaveTagsCommand = new AsyncRelayCommand(SaveTagsAsync);
    }

    public ObservableCollection<CalendarDay> CalendarDays { get; } = [];
    public ObservableCollection<CalendarEvent> SelectedDayEvents { get; } = [];
    public ObservableCollection<CalendarEvent> SevenDayEvents { get; } = [];
    public ObservableCollection<CalendarEvent> TodoEvents { get; } = [];
    public ObservableCollection<CalendarEvent> CompletedTodoEvents { get; } = [];
    public ObservableCollection<CalendarTag> Tags { get; } = [];
    public ObservableCollection<string> CalendarNames { get; } = ["primary"];

    public RelayCommand PreviousMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand PreviousYearCommand { get; }
    public RelayCommand NextYearCommand { get; }
    public RelayCommand TodayCommand { get; }
    public RelayCommand NewEventCommand { get; }
    public AsyncRelayCommand SaveEventCommand { get; }
    public AsyncRelayCommand DeleteEventCommand { get; }
    public AsyncRelayCommand MarkSelectedTodoDoneCommand { get; }
    public AsyncRelayCommand SyncCommand { get; }
    public AsyncRelayCommand BrowseOAuthClientCommand { get; }
    public AsyncRelayCommand AuthorizeCommand { get; }
    public AsyncRelayCommand ClearTokensCommand { get; }
    public AsyncRelayCommand SaveTagsCommand { get; }

    public string MonthTitle => CurrentMonth.ToString("yyyy/MM", CultureInfo.InvariantCulture);
    public string JapaneseMonthTitle => $"{CurrentMonth:yyyy}年（{FormatJapaneseEra(CurrentMonth)}） {CurrentMonth.Month}月";
    public string CalendarStatusText => FormatCalendarStatus(DateTime.Today);

    public DateTime CurrentMonth
    {
        get => _currentMonth;
        set
        {
            if (SetProperty(ref _currentMonth, new DateTime(value.Year, value.Month, 1)))
            {
                OnPropertyChanged(nameof(MonthTitle));
                OnPropertyChanged(nameof(JapaneseMonthTitle));
                _ = RefreshCalendarAsync();
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
                RefreshSelectedDayEvents();
                RefreshSevenDayEvents();
                if (value is not null)
                {
                    StartDate = value.Date;
                    EndDate = value.Date;
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

    public string OAuthClientJsonPath
    {
        get => _oauthClientJsonPath;
        set => SetProperty(ref _oauthClientJsonPath, value);
    }

    public async Task InitializeAsync()
    {
        await _repository.InitializeAsync();
        _settings = await _repository.LoadSettingsAsync();
        OAuthClientJsonPath = _settings.OAuthClientJsonPath ?? "";
        await ReloadTagsAsync();
        CurrentMonth = _settings.DisplayMonth;
        await RefreshCalendarAsync();
        Status = "準備完了";
    }

    public void NewEvent()
    {
        BeginNewEvent(SelectedDay?.Date ?? DateTime.Today);
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
        IsAllDay = true;
        Status = "新しいスケジュールを入力してください。";
    }

    public async Task SaveCurrentEventAsync()
    {
        await SaveEventAsync();
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
            CalendarId = _settings.ActiveCalendarId,
            Start = new DateTimeOffset(dueDate.Date),
            End = new DateTimeOffset(dueDate.Date.AddDays(1)),
            IsAllDay = true,
            IsDirty = true,
            IsDeleted = false,
            IsTodoLike = true
        };

        await _repository.SaveEventAsync(todoEvent);
        await RefreshCalendarAsync();
        Status = "ToDoを保存しました。同期するとGoogleカレンダーへ反映されます。";
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
        ApplyDisplayColors(events);
        return events.Where(IsVisible).OrderBy(e => e.Start).ThenBy(e => e.Title).ToArray();
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

    private async Task ReloadTagsAsync()
    {
        Tags.Clear();
        foreach (var tag in await _repository.LoadTagsAsync())
        {
            Tags.Add(tag);
        }
    }

    private async Task RefreshCalendarAsync()
    {
        _settings.DisplayMonth = CurrentMonth;
        await _repository.SaveSettingsAsync(_settings);

        var (gridStart, gridEnd) = DateRangeHelper.MonthGridRange(CurrentMonth);
        var events = await _repository.LoadEventsAsync(new DateTimeOffset(gridStart), new DateTimeOffset(gridEnd));
        ApplyDisplayColors(events);
        _visibleEvents = events.Where(IsVisible).ToArray();

        CalendarDays.Clear();
        for (var date = gridStart; date < gridEnd; date = date.AddDays(1))
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

            CalendarDays.Add(day);
        }

        SelectedDay ??= CalendarDays.FirstOrDefault(d => d.Date == DateTime.Today) ?? CalendarDays.FirstOrDefault();
        RefreshSelectedDayEvents();
        RefreshSevenDayEvents();
        RefreshTodos(events);
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
        return displayTag?.IsVisible ?? true;
    }

    private void ChangeMonth(int monthOffset)
    {
        CurrentMonth = CurrentMonth.AddMonths(monthOffset);
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

    private void LoadEditor(CalendarEvent? calendarEvent)
    {
        if (calendarEvent is null)
        {
            return;
        }

        Title = calendarEvent.Title;
        Description = calendarEvent.Description ?? "";
        Location = calendarEvent.Location ?? "";
        StartDate = calendarEvent.Start.Date;
        EndDate = calendarEvent.IsAllDay ? calendarEvent.End.Date.AddDays(-1) : calendarEvent.End.Date;
        StartTime = calendarEvent.Start.ToString("HH:mm", CultureInfo.InvariantCulture);
        EndTime = calendarEvent.End.ToString("HH:mm", CultureInfo.InvariantCulture);
        IsAllDay = calendarEvent.IsAllDay;
    }

    private async Task SaveEventAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Status = "件名を入力してください。";
            return;
        }

        var calendarEvent = SelectedEvent ?? new CalendarEvent();
        calendarEvent.Title = Title.Trim();
        calendarEvent.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        calendarEvent.Location = string.IsNullOrWhiteSpace(Location) ? null : Location.Trim();
        calendarEvent.CalendarId = _settings.ActiveCalendarId;
        calendarEvent.IsAllDay = IsAllDay;
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
                return;
            }

            calendarEvent.Start = new DateTimeOffset(StartDate.Date.Add(startTime));
            calendarEvent.End = new DateTimeOffset(EndDate.Date.Add(endTime));
            if (calendarEvent.End <= calendarEvent.Start)
            {
                Status = "終了日時は開始日時より後にしてください。";
                return;
            }
        }

        await _repository.SaveEventAsync(calendarEvent);
        SelectedEvent = calendarEvent;
        await RefreshCalendarAsync();
        Status = "スケジュールを保存しました。同期するとGoogleカレンダーへ反映されます。";
    }

    private async Task DeleteEventAsync()
    {
        if (SelectedEvent is null)
        {
            return;
        }

        await _repository.DeleteEventAsync(SelectedEvent);
        SelectedEvent = null;
        await RefreshCalendarAsync();
        Status = "スケジュールを削除しました。同期するとGoogleカレンダーへ反映されます。";
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
        Status = "Google認証が完了しました。";
    }

    private async Task SyncAsync()
    {
        await SaveOAuthPathAsync();
        Status = "Googleカレンダーと同期中...";
        var result = await _syncService.SyncAsync(_settings);
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
        await _repository.SaveSettingsAsync(_settings);
    }
}
