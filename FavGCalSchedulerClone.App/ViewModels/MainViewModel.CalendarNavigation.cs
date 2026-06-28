using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FavGCalSchedulerClone.App.Commands;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Win32;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    internal bool IsCalendarMonthCached(DateTime month) => TryGetCalendarCache(month) is not null;

    public async Task GoToTodayAsync()
    {
        _pendingSelectedDate = DateTime.Today;
        _navigationAnchorDate = DateTime.Today;
        SetCurrentMonthWithoutRefreshing(DateTime.Today);
        ShowImmediateCalendarShellForMonth(CurrentMonth);
        await RefreshCalendarAsync(invalidateCache: false, refreshTodos: false);
        Status = "今日を表示しました。";
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
}
