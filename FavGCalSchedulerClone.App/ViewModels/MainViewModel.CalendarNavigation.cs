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
        ScheduleDisplayMonthPersistence(CurrentMonth);
        ShowImmediateCalendarShellForMonth(CurrentMonth);
        await RefreshCalendarAsync(invalidateCache: false, refreshTodos: false);
        Status = "今日を表示しました。";
    }

    public async Task NavigateToDateAsync(DateTime targetDate)
    {
        _pendingSelectedDate = targetDate.Date;
        _navigationAnchorDate = targetDate.Date;
        SetCurrentMonthWithoutRefreshing(targetDate.Date);
        ScheduleDisplayMonthPersistence(CurrentMonth);
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

    public bool UpdateMonthLaneCapacity(int capacity)
    {
        var normalizedCapacity = Math.Max(CalendarSegmentLayoutService.MinimumLanes, capacity);
        if (!IsMonthView || normalizedCapacity == _monthLaneCapacity)
        {
            return false;
        }

        _monthLaneCapacity = normalizedCapacity;
        ApplySegmentLayout(CalendarDays, _monthLaneCapacity);
        UpdateSegmentSelection();
        return true;
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
        var totalStopwatch = Stopwatch.StartNew();
        request.CancellationToken.ThrowIfCancellationRequested();
        if (request.SaveDisplayMonth)
        {
            ScheduleDisplayMonthPersistence(request.Month);
        }

        var cacheStopwatch = Stopwatch.StartNew();
        var snapshot = TryGetCalendarCache(request.Month);
        var cacheHit = snapshot is not null;
        var cacheLookupMilliseconds = cacheStopwatch.ElapsedMilliseconds;
        var snapshotStopwatch = Stopwatch.StartNew();
        snapshot ??= await LoadCalendarSnapshotAsync(request.Month, request.CancellationToken);
        var snapshotMilliseconds = snapshotStopwatch.ElapsedMilliseconds;
        if (!IsLatestCalendarRefresh(request))
        {
            return;
        }

        var applyStopwatch = Stopwatch.StartNew();
        var appliedSnapshot = ApplyCalendarSnapshotIfNeeded(
            snapshot,
            CreateCalendarCacheKey(request.Month),
            request.PendingSelectedDate,
            clearPendingSelectedDate: true);
        StoreCalendarCache(snapshot);
        _logger?.LogInfo(
            $"Calendar navigation {request.Month:yyyy-MM}: cacheHit={cacheHit}, cacheLookup={cacheLookupMilliseconds}ms, snapshot={snapshotMilliseconds}ms, applyUi={applyStopwatch.ElapsedMilliseconds}ms, applied={appliedSnapshot}, total={totalStopwatch.ElapsedMilliseconds}ms");
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
        var context = CreateCalendarSnapshotBuildContext();
        var (gridStart, gridEnd) = DateRangeHelper.MonthGridRange(month, context.WeekStartsOnMonday);
        var dataWindow = await GetCalendarDataWindowAsync(month, gridStart, gridEnd, context.WeekStartsOnMonday, cancellationToken);
        var snapshot = await Task.Run(
            () => BuildCalendarSnapshot(month, gridStart, gridEnd, dataWindow, context, cancellationToken),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot;
    }

    private async Task<CalendarDataWindow> GetCalendarDataWindowAsync(
        DateTime month,
        DateTime gridStart,
        DateTime gridEnd,
        bool weekStartsOnMonday,
        CancellationToken cancellationToken)
    {
        var dataVersion = Volatile.Read(ref _calendarDataVersion);
        lock (_calendarCacheLock)
        {
            if (_calendarDataWindow is { } existing
                && existing.DataVersion == dataVersion
                && existing.WeekStartsOnMonday == weekStartsOnMonday
                && existing.RangeStart <= gridStart
                && existing.RangeEnd >= gridEnd)
            {
                return existing;
            }
        }

        if (BeforeLoadCalendarSnapshotAsync is not null)
        {
            await BeforeLoadCalendarSnapshotAsync(month, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var (rangeStart, _) = DateRangeHelper.MonthGridRange(month.AddMonths(-12), weekStartsOnMonday);
        var (_, rangeEnd) = DateRangeHelper.MonthGridRange(month.AddMonths(12), weekStartsOnMonday);
        var storedEvents = await _repository.LoadEventsAsync(
            new DateTimeOffset(rangeStart),
            new DateTimeOffset(rangeEnd),
            includeDeleted: true,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var dataWindow = await Task.Run(
            () => BuildCalendarDataWindow(rangeStart, rangeEnd, weekStartsOnMonday, dataVersion, storedEvents, cancellationToken),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_calendarCacheLock)
        {
            if (dataVersion == _calendarDataVersion)
            {
                _calendarDataWindow = dataWindow;
            }
        }

        return dataWindow;
    }

    private CalendarDataWindow BuildCalendarDataWindow(
        DateTime rangeStart,
        DateTime rangeEnd,
        bool weekStartsOnMonday,
        long dataVersion,
        IReadOnlyList<CalendarEvent> storedEvents,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expandedEvents = RecurrenceExpansionService
            .ExpandForRange(storedEvents, new DateTimeOffset(rangeStart), new DateTimeOffset(rangeEnd))
            .ToArray();
        var eventsByDate = new Dictionary<DateTime, List<CalendarEvent>>();
        foreach (var calendarEvent in expandedEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var firstDate = calendarEvent.Start.Date < rangeStart ? rangeStart : calendarEvent.Start.Date;
            var finalDate = calendarEvent.End.Date >= rangeEnd ? rangeEnd.AddDays(-1) : calendarEvent.End.Date;
            for (var date = firstDate; date <= finalDate; date = date.AddDays(1))
            {
                if (!DateRangeHelper.OccursOn(calendarEvent, date))
                {
                    continue;
                }

                if (!eventsByDate.TryGetValue(date, out var events))
                {
                    events = [];
                    eventsByDate.Add(date, events);
                }

                events.Add(calendarEvent);
            }
        }

        return new CalendarDataWindow(
            rangeStart,
            rangeEnd,
            weekStartsOnMonday,
            dataVersion,
            storedEvents,
            expandedEvents,
            eventsByDate.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<CalendarEvent>)pair.Value));
    }

    private CalendarRefreshSnapshot BuildCalendarSnapshot(
        DateTime month,
        DateTime gridStart,
        DateTime gridEnd,
        CalendarDataWindow dataWindow,
        CalendarSnapshotBuildContext context,
        CancellationToken cancellationToken)
    {
        BeforeBuildCalendarSnapshot?.Invoke(month, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var calendarEvents = Enumerable.Range(0, (gridEnd - gridStart).Days)
            .SelectMany(offset => dataWindow.EventsByDate.TryGetValue(gridStart.AddDays(offset), out var events) ? events : [])
            .Distinct()
            .Where(item => IsInVisibleCalendar(item, context))
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var visibleEvents = calendarEvents.Where(item => IsVisible(item, context)).ToArray();
        ApplyDisplayColors(visibleEvents, context);
        return new CalendarRefreshSnapshot(month, gridStart, gridEnd, dataWindow.StoredEvents, calendarEvents, visibleEvents);
    }

    private void ApplyCalendarSnapshot(
        CalendarRefreshSnapshot snapshot,
        DateTime? pendingSelectedDate,
        bool clearPendingSelectedDate)
    {
        BeforeApplyCalendarSnapshot?.Invoke(snapshot);
        _storedEvents = snapshot.StoredEvents;
        _dayDirectiveEvents = snapshot.DayDirectiveEvents;
        _visibleEvents = snapshot.VisibleEvents;

        EnsureCalendarDayCapacity((snapshot.GridEnd - snapshot.GridStart).Days);
        var index = 0;
        for (var date = snapshot.GridStart; date < snapshot.GridEnd; date = date.AddDays(1), index++)
        {
            var day = CalendarDays[index];
            UpdateCalendarDayShell(day, date, clearEvents: true);
            day.IsWorkdayOverride = TagService.HasWorkdayOverride(_dayDirectiveEvents, date);
            day.IsHoliday = TagService.HasHolidayWithoutWorkdayOverride(_dayDirectiveEvents, date);
        }

        ApplySegmentLayout(CalendarDays, IsMonthView ? _monthLaneCapacity : CalendarSegmentLayoutService.MaxLanes);

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

    private bool ApplyCalendarSnapshotIfNeeded(
        CalendarRefreshSnapshot snapshot,
        CalendarCacheKey key,
        DateTime? pendingSelectedDate,
        bool clearPendingSelectedDate)
    {
        if (ReferenceEquals(_lastAppliedCalendarSnapshot, snapshot)
            && EqualityComparer<CalendarCacheKey?>.Default.Equals(_lastAppliedCalendarSnapshotKey, key)
            && (pendingSelectedDate is null || SelectedDay?.Date == pendingSelectedDate.Value.Date))
        {
            if (clearPendingSelectedDate
                && pendingSelectedDate is not null
                && SelectedDay?.Date == pendingSelectedDate.Value.Date)
            {
                _pendingSelectedDate = null;
            }

            return false;
        }

        ApplyCalendarSnapshot(snapshot, pendingSelectedDate, clearPendingSelectedDate);
        _lastAppliedCalendarSnapshot = snapshot;
        _lastAppliedCalendarSnapshotKey = key;
        return true;
    }

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
            await RefreshCalendarSafelyAsync(BeginCalendarRefresh(saveDisplayMonth: false, refreshTodos: false));
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
            ApplyCalendarSnapshotIfNeeded(
                snapshot,
                CreateCalendarCacheKey(month),
                _pendingSelectedDate,
                clearPendingSelectedDate: false);
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

    private void ScheduleDisplayMonthPersistence(DateTime month)
    {
        _settings.DisplayMonth = new DateTime(month.Year, month.Month, 1);
        var version = Interlocked.Increment(ref _displayMonthPersistenceVersion);
        var replacement = new CancellationTokenSource();
        var prior = Interlocked.Exchange(ref _displayMonthPersistenceCts, replacement);
        prior?.Cancel();
        prior?.Dispose();
        _ = PersistDisplayMonthAfterDelayAsync(_settings.DisplayMonth, version, replacement.Token);
    }

    private async Task PersistDisplayMonthAfterDelayAsync(DateTime month, long version, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            await PersistDisplayMonthAsync(month, version, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal async Task FlushDisplayMonthPersistenceAsync()
    {
        var cancellation = Interlocked.Exchange(ref _displayMonthPersistenceCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        var version = Volatile.Read(ref _displayMonthPersistenceVersion);
        await PersistDisplayMonthAsync(_settings.DisplayMonth, version, CancellationToken.None);
    }

    private async Task PersistDisplayMonthAsync(DateTime month, long version, CancellationToken cancellationToken)
    {
        await _displayMonthPersistenceGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _displayMonthPersistenceVersion))
            {
                return;
            }

            BeforeSaveDisplayMonth?.Invoke(month);
            await _repository.SaveSettingsAsync(_settings);
            _logger?.LogInfo($"DisplayMonth persisted: {month:yyyy-MM}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, $"Failed to persist DisplayMonth {month:yyyy-MM}");
        }
        finally
        {
            _displayMonthPersistenceGate.Release();
        }
    }

    private void EnsureCalendarDayCapacity(int count)
    {
        if (CalendarDays.Count == count)
        {
            return;
        }

        _calendarDays.ReplaceAll(Enumerable.Range(0, count).Select(_ => new CalendarDay()));
    }

    private void UpdateCalendarDayShell(CalendarDay day, DateTime date, bool clearEvents)
    {
        day.Date = date;
        day.IsCurrentMonth = date.Month == CurrentMonth.Month;
        day.IsWorkdayOverride = false;
        day.IsHoliday = false;
        if (clearEvents)
        {
            day.ReplaceEvents([]);
            day.HiddenEventCount = 0;
            day.ReplaceSegments([]);
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
        var context = CreateCalendarSnapshotBuildContext();
        var months = new[] { request.Month.AddMonths(-1), request.Month.AddMonths(1) };
        await Task.WhenAll(months.Select(month => PrefetchMonthAsync(request, context, month)));
    }

    private async Task PrefetchMonthAsync(
        CalendarRefreshRequest request,
        CalendarSnapshotBuildContext context,
        DateTime month)
    {
        try
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            var key = CreateCalendarCacheKey(month, context);
            lock (_calendarCacheLock)
            {
                if (_calendarCache.ContainsKey(key))
                {
                    return;
                }
            }

            Debug.WriteLine($"Calendar prefetch start: {month:yyyy-MM}, generation={request.Generation}");
            var (gridStart, gridEnd) = DateRangeHelper.MonthGridRange(month, context.WeekStartsOnMonday);
            var dataWindow = await GetCalendarDataWindowAsync(month, gridStart, gridEnd, context.WeekStartsOnMonday, request.CancellationToken);
            request.CancellationToken.ThrowIfCancellationRequested();
            var snapshot = await Task.Run(
                () => BuildCalendarSnapshot(month, gridStart, gridEnd, dataWindow, context, request.CancellationToken),
                request.CancellationToken);
            request.CancellationToken.ThrowIfCancellationRequested();
            lock (_calendarCacheLock)
            {
                if (!IsLatestCalendarRefresh(request) || _calendarCache.ContainsKey(key))
                {
                    Debug.WriteLine($"Calendar prefetch discarded: {month:yyyy-MM}, generation={request.Generation}");
                    return;
                }

                StoreCalendarCache(snapshot, key);
            }

            Debug.WriteLine($"Calendar prefetch complete: {month:yyyy-MM}, generation={request.Generation}");
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"Calendar prefetch canceled: generation={request.Generation}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
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
        return new CalendarCacheKey(normalizedMonth, context.WeekStartsOnMonday, visibleCalendars, Volatile.Read(ref _calendarDataVersion));
    }

    private CalendarRefreshSnapshot? TryGetCalendarCache(DateTime month)
    {
        var key = CreateCalendarCacheKey(month);
        lock (_calendarCacheLock)
        {
            if (!_calendarCache.TryGetValue(key, out var snapshot))
            {
                return null;
            }

            Debug.WriteLine($"Calendar cache hit: {key.Month:yyyy-MM}, weekStartsOnMonday={key.WeekStartsOnMonday}, calendars={key.VisibleCalendarIds}");
            return snapshot;
        }
    }

    private void StoreCalendarCache(CalendarRefreshSnapshot snapshot)
    {
        lock (_calendarCacheLock)
        {
            StoreCalendarCache(snapshot, CreateCalendarCacheKey(snapshot.Month));
        }
    }

    private void StoreCalendarCache(CalendarRefreshSnapshot snapshot, CalendarCacheKey key)
    {
        _calendarCache[key] = snapshot;
        if (_calendarCache.Count <= CalendarSnapshotCacheCapacity)
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
            if (_calendarCache.Count <= CalendarSnapshotCacheCapacity)
            {
                break;
            }
        }
    }

    private void InvalidateCalendarCache()
    {
        lock (_calendarCacheLock)
        {
            _calendarCache.Clear();
            _calendarDataWindow = null;
            Interlocked.Increment(ref _calendarDataVersion);
        }
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
        ScheduleDisplayMonthPersistence(CurrentMonth);
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
        var anchor = anchorDate?.Date ?? SelectedDay?.Date ?? CurrentMonth;
        var days = CalendarVisibleRangeService
            .GetVisibleDates(CurrentViewMode, CalendarDays, anchor, _settings.WeekStartsOnMonday)
            .Select(FindOrCreateCalendarDay)
            .ToArray();
        _visibleCalendarDays.ReplaceAll(days);
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

        ApplySegmentLayout([day], IsMonthView ? _monthLaneCapacity : CalendarSegmentLayoutService.MaxLanes);

        return day;
    }

    private void ApplySegmentLayout(IReadOnlyList<CalendarDay> days, int laneCapacity)
    {
        var layoutResult = CalendarSegmentLayoutService.PopulateSegments(days, _visibleEvents, laneCapacity);
        foreach (var day in days)
        {
            var layoutDay = layoutResult.GetDay(day.Date);
            day.ReplaceEvents(layoutDay.VisibleEvents);
            day.HiddenEventCount = layoutDay.HiddenEventCount;
        }
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
