using System.Diagnostics;
using System.Text.Json;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public sealed class ReminderNotificationService : IDisposable
{
    private const string ReminderStateKey = "reminder:fired";
    private const string ReminderSnoozeKey = "reminder:snoozed";
    private const string ReminderHistoryKey = "reminder:history";
    private const string ReminderLastErrorKey = "reminder:last-error";
    private const string ReminderDiagnosticsKey = "reminder:diagnostics";
    private const int MaxHistoryCount = 50;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DiagnosticsPersistenceInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailureAggregationWindow = TimeSpan.FromMinutes(5);
    private readonly CalendarRepository _repository;
    private readonly Timer _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReminderNotifier? _notifier;
    private bool _disposed;
    private bool _isRunning;
    private bool _maintenancePaused;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _lastDiagnosticsSavedAt;
    private string? _lastPersistedDiagnosticsSignature;
    private ReminderMonitoringSnapshot _diagnostics = ReminderMonitoringSnapshot.Stopped;

    public ReminderNotificationService(CalendarRepository repository, IReminderNotifier? notifier = null)
    {
        _repository = repository;
        _notifier = notifier;
        _timer = new Timer(_ => _ = CheckDueRemindersAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Func<ReminderNotification, Task>? ReminderTriggered;
    public bool IsRunning => _isRunning;
    public ReminderMonitoringSnapshot CurrentDiagnostics => _diagnostics;

    public void SetNotifier(IReminderNotifier notifier)
    {
        _notifier = notifier;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _isRunning)
        {
            return;
        }

        _isRunning = true;
        _startedAt = DateTimeOffset.Now;
        try
        {
            await CheckDueRemindersAsync(DateTimeOffset.Now, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed)
            {
                _isRunning = false;
                UpdateRuntimeState(null);
                return;
            }

            try
            {
                _timer.Change(CheckInterval, CheckInterval);
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                _isRunning = false;
                UpdateRuntimeState(null);
                return;
            }

            UpdateRuntimeState(DateTimeOffset.Now.Add(CheckInterval));
            Debug.WriteLine("通知監視を開始しました");
        }
        catch
        {
            _isRunning = false;
            UpdateRuntimeState(null);
            throw;
        }
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _isRunning = false;
        UpdateRuntimeState(null);
        _ = SaveDiagnosticsSafelyAsync();
    }

    public async Task<bool> PauseForMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        var wasRunning = _isRunning;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _isRunning = false;
        Volatile.Write(ref _maintenancePaused, true);
        UpdateRuntimeState(null);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveDiagnosticsAsync(force: true);
        }
        finally
        {
            _gate.Release();
        }

        return wasRunning;
    }

    public async Task ResumeAfterMaintenanceAsync(bool resumeMonitoring, CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _maintenancePaused, false);
        if (resumeMonitoring)
        {
            await StartAsync(cancellationToken);
        }
        else
        {
            UpdateRuntimeState(null);
        }
    }

    public async Task<ReminderMonitoringSnapshot> LoadDiagnosticsAsync()
    {
        var json = await _repository.LoadSettingValueAsync(ReminderDiagnosticsKey);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var persisted = JsonSerializer.Deserialize<ReminderMonitoringSnapshot>(json);
                if (persisted is not null)
                {
                    _lastDiagnosticsSavedAt = persisted.LastCheckAt;
                    _lastPersistedDiagnosticsSignature = CreateDiagnosticsSignature(persisted);
                    if (_diagnostics.LastCheckAt is null || persisted.LastCheckAt > _diagnostics.LastCheckAt)
                    {
                        _diagnostics = persisted;
                    }
                }
            }
            catch (JsonException ex)
            {
                Debug.WriteLine(ex);
            }
        }

        UpdateRuntimeState(_isRunning ? DateTimeOffset.Now.Add(CheckInterval) : null);
        return _diagnostics;
    }

    public async Task CheckDueRemindersAsync(DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        await CheckDueRemindersAsync(now, includeAllCandidates: false, forceDiagnosticsSave: false, cancellationToken);
    }

    public async Task CheckDueRemindersDetailedAsync(DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        await CheckDueRemindersAsync(now, includeAllCandidates: true, forceDiagnosticsSave: true, cancellationToken);
    }

    private async Task CheckDueRemindersAsync(
        DateTimeOffset? now,
        bool includeAllCandidates,
        bool forceDiagnosticsSave,
        CancellationToken cancellationToken)
    {
        try
        {
            await CheckDueRemindersCoreAsync(now, includeAllCandidates, forceDiagnosticsSave, cancellationToken);
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            var error = $"{DateTimeOffset.Now:O} {ex}";
            _diagnostics = _diagnostics with { LastError = error, NextCheckAt = _isRunning ? DateTimeOffset.Now.Add(CheckInterval) : null };
            try
            {
                await _repository.SaveSettingValueAsync(ReminderLastErrorKey, error);
            }
            catch (Exception persistenceException)
            {
                Debug.WriteLine(persistenceException);
            }

            await SaveDiagnosticsSafelyAsync();
        }
    }

    private async Task CheckDueRemindersCoreAsync(
        DateTimeOffset? now,
        bool includeAllCandidates,
        bool forceDiagnosticsSave,
        CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        var current = now ?? DateTimeOffset.Now;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed || Volatile.Read(ref _maintenancePaused))
            {
                return;
            }

            var fired = await LoadFiredStateAsync();
            var snoozed = await LoadSnoozeStateAsync();
            PruneFiredState(fired, current);
            PruneSnoozeState(snoozed, current.AddDays(-7));

            var windowStart = current.AddDays(-1);
            var windowEnd = current.AddDays(30);
            var storedEvents = await _repository.LoadEventsAsync(windowStart, windowEnd, includeDeleted: false);
            var expandedEvents = RecurrenceExpansionService.ExpandForRange(storedEvents, windowStart, windowEnd);
            var candidateDiagnostics = new List<ReminderCandidateDiagnostic>();
            var dueNotifications = new List<ReminderNotification>();
            var reminderConfiguredCount = 0;
            var noReminderCount = 0;
            var firedExcludedCount = 0;
            var snoozedExcludedCount = 0;

            foreach (var calendarEvent in expandedEvents)
            {
                if (calendarEvent.IsTodoLike)
                {
                    reminderConfiguredCount++;
                    var todoNotification = CreateTodoReminderNotification(calendarEvent);
                    var todoIsFired = fired.ContainsKey(todoNotification.OccurrenceKey);
                    var todoSnoozedUntil = TryGetSnoozedUntil(snoozed, todoNotification.OccurrenceKey);
                    var todoIsDue = todoNotification.RemindAt <= current;
                    string todoReason;
                    if (calendarEvent.IsTodoDone)
                    {
                        todoReason = "完了ToDo";
                    }
                    else if (todoSnoozedUntil is not null && todoSnoozedUntil > current)
                    {
                        snoozedExcludedCount++;
                        todoReason = "スヌーズ中";
                    }
                    else if (todoIsFired && todoSnoozedUntil is null)
                    {
                        firedExcludedCount++;
                        todoReason = "発火済み";
                    }
                    else if (!todoIsDue)
                    {
                        todoReason = "ToDo固定通知時刻前";
                    }
                    else if (current >= TodoReminderPolicy.GetDueDayEnd(calendarEvent.Start.Date))
                    {
                        todoReason = "期限日終了済み";
                    }
                    else
                    {
                        todoReason = todoSnoozedUntil is not null ? "スヌーズ期限到達" : "ToDo固定通知対象";
                        dueNotifications.Add(todoNotification);
                    }

                    candidateDiagnostics.Add(CreateCandidateDiagnostic(
                        calendarEvent, current, null, todoNotification, todoIsDue, todoIsFired, todoSnoozedUntil, todoReason));
                    continue;
                }

                var googleReminderReason = GetGoogleReminderDiagnosticReason(calendarEvent);
                var appReminderMinutes = calendarEvent.EffectiveAppReminderMinutesBeforeStart;
                if (appReminderMinutes.Count != 1
                    || calendarEvent.ReminderMinutesBeforeStart != appReminderMinutes[0]
                    || !calendarEvent.IsAppReminderEnabled)
                {
                    if (appReminderMinutes.Count == 0)
                    {
                        noReminderCount++;
                        if (includeAllCandidates || calendarEvent.IsGoogleEmailReminderEnabled || calendarEvent.GoogleReminderMetadata?.HasGoogleReminder == true)
                        {
                            candidateDiagnostics.Add(CreateCandidateDiagnostic(
                                calendarEvent,
                                current,
                                null,
                                null,
                                false,
                                false,
                                null,
                                googleReminderReason ?? "アプリ内通知なし"));
                        }

                        continue;
                    }

                    reminderConfiguredCount++;
                    foreach (var expandedReminderMinutes in appReminderMinutes)
                    {
                        var expandedNotification = CreateReminderNotification(calendarEvent, current, expandedReminderMinutes);
                        if (expandedNotification is null)
                        {
                            candidateDiagnostics.Add(CreateCandidateDiagnostic(calendarEvent, current, expandedReminderMinutes, null, false, false, null, "通知情報を作成できません"));
                            continue;
                        }

                        var expandedIsFired = fired.ContainsKey(expandedNotification.OccurrenceKey);
                        var expandedSnoozedUntil = TryGetSnoozedUntil(snoozed, expandedNotification.OccurrenceKey);
                        var expandedIsDue = expandedNotification.RemindAt <= current;
                        string expandedReason;
                        if (calendarEvent.IsTodoDone)
                        {
                            expandedReason = "完了ToDo";
                        }
                        else if (expandedSnoozedUntil is not null && expandedSnoozedUntil > current)
                        {
                            snoozedExcludedCount++;
                            expandedReason = "スヌーズ中";
                        }
                        else if (expandedIsFired && expandedSnoozedUntil is null)
                        {
                            firedExcludedCount++;
                            expandedReason = "発火済み";
                        }
                        else if (!expandedIsDue)
                        {
                            expandedReason = googleReminderReason ?? "通知時刻未到達";
                        }
                        else if (IsPastForReminder(calendarEvent, current))
                        {
                            expandedReason = calendarEvent.IsAllDay ? "終了済みの予定" : "開始済みの予定";
                        }
                        else
                        {
                            expandedReason = expandedSnoozedUntil is not null ? "スヌーズ期限到達" : "通知対象";
                            dueNotifications.Add(expandedNotification);
                        }

                        candidateDiagnostics.Add(CreateCandidateDiagnostic(calendarEvent, current, expandedReminderMinutes, expandedNotification, expandedIsDue, expandedIsFired, expandedSnoozedUntil, expandedReason));
                    }

                    continue;
                }

                if (calendarEvent.ReminderMinutesBeforeStart is not int reminderMinutes)
                {
                    noReminderCount++;
                    if (includeAllCandidates || calendarEvent.GoogleReminderMetadata?.HasGoogleReminder == true)
                    {
                        candidateDiagnostics.Add(CreateCandidateDiagnostic(
                            calendarEvent,
                            current,
                            null,
                            null,
                            false,
                            false,
                            null,
                            googleReminderReason ?? "通知設定なし"));
                    }
                    continue;
                }

                if (!calendarEvent.IsAppReminderEnabled)
                {
                    noReminderCount++;
                    if (includeAllCandidates || calendarEvent.IsGoogleEmailReminderEnabled || calendarEvent.GoogleReminderMetadata?.HasGoogleReminder == true)
                    {
                        candidateDiagnostics.Add(CreateCandidateDiagnostic(
                            calendarEvent,
                            current,
                            reminderMinutes,
                            null,
                            false,
                            false,
                            null,
                            "アプリ内通知なし"));
                    }
                    continue;
                }

                reminderConfiguredCount++;
                var notification = CreateReminderNotification(calendarEvent, current);
                if (notification is null)
                {
                    candidateDiagnostics.Add(CreateCandidateDiagnostic(calendarEvent, current, reminderMinutes, null, false, false, null, "通知情報を作成できません"));
                    continue;
                }

                var isFired = fired.ContainsKey(notification.OccurrenceKey);
                var snoozedUntil = TryGetSnoozedUntil(snoozed, notification.OccurrenceKey);
                var isDue = notification.RemindAt <= current;
                string reason;
                if (calendarEvent.IsTodoDone)
                {
                    reason = "完了ToDo";
                }
                else if (snoozedUntil is not null && snoozedUntil > current)
                {
                    snoozedExcludedCount++;
                    reason = "スヌーズ中";
                }
                else if (isFired && snoozedUntil is null)
                {
                    firedExcludedCount++;
                    reason = "既に発火済み";
                }
                else if (!isDue)
                {
                    reason = googleReminderReason ?? "通知時刻未到達";
                }
                else if (IsPastForReminder(calendarEvent, current))
                {
                    reason = calendarEvent.IsAllDay ? "終了済みの予定" : "開始済みの予定";
                }
                else
                {
                    reason = snoozedUntil is not null ? "スヌーズ期限到達" : "通知対象";
                    dueNotifications.Add(notification);
                }

                candidateDiagnostics.Add(CreateCandidateDiagnostic(calendarEvent, current, reminderMinutes, notification, isDue, isFired, snoozedUntil, reason));
            }

            dueNotifications = dueNotifications.OrderBy(item => item.RemindAt).ThenBy(item => item.EventStart).ToList();
            var succeededCount = 0;
            var failedCount = 0;

            foreach (var notification in dueNotifications)
            {
                var result = await TryDispatchNotificationAsync(notification, cancellationToken);
                if (result.Succeeded)
                {
                    succeededCount++;
                    fired[notification.OccurrenceKey] = current.ToString("O");
                    snoozed.Remove(notification.OccurrenceKey);
                    await SaveFiredStateAsync(fired);
                    await SaveSnoozeStateAsync(snoozed);
                    await AddHistoryAsync(notification, current, null, deliverySucceeded: true, deliveryMethod: result.DeliveryMethod, result.UsedMessageBoxFallback, result.MessageBoxRole, result.ToastVerified, result.ToastStatus, result.SoundStatus, result.SoundError, deliveryError: null);
                }
                else
                {
                    failedCount++;
                    var diagnosticIndex = candidateDiagnostics.FindIndex(item => item.OccurrenceKey == notification.OccurrenceKey);
                    if (diagnosticIndex >= 0)
                    {
                        candidateDiagnostics[diagnosticIndex] = candidateDiagnostics[diagnosticIndex] with
                        {
                            Reason = "通知エラー",
                            ErrorMessage = result.ErrorMessage
                        };
                    }
                    await AddHistoryAsync(notification, current, null, deliverySucceeded: false, deliveryMethod: result.DeliveryMethod, result.UsedMessageBoxFallback, result.MessageBoxRole, result.ToastVerified, result.ToastStatus, result.SoundStatus, result.SoundError, deliveryError: result.ErrorMessage);
                    await _repository.SaveSettingValueAsync(ReminderLastErrorKey, $"{current:O} {result.ErrorMessage}");
                }
            }

            await SaveFiredStateAsync(fired);
            await SaveSnoozeStateAsync(snoozed);
            var lastError = await _repository.LoadSettingValueAsync(ReminderLastErrorKey);
            _diagnostics = new ReminderMonitoringSnapshot(
                _isRunning, _startedAt, current, _isRunning ? current.Add(CheckInterval) : null,
                storedEvents.Count, expandedEvents.Count, reminderConfiguredCount, noReminderCount, expandedEvents.Count,
                dueNotifications.Count, firedExcludedCount, snoozedExcludedCount, succeededCount, failedCount,
                lastError, candidateDiagnostics);
            await SaveDiagnosticsAsync(forceDiagnosticsSave, current);
            Debug.WriteLine($"""
                Reminder check current={current:O}
                  storedEvents={storedEvents.Count} expandedEvents={expandedEvents.Count}
                  reminderConfigured={reminderConfiguredCount} noReminder={noReminderCount} candidates={expandedEvents.Count} savedCandidates={candidateDiagnostics.Count} detailed={includeAllCandidates} due={dueNotifications.Count}
                  firedExcluded={firedExcludedCount} snoozedExcluded={snoozedExcludedCount}
                  succeeded={succeededCount} failed={failedCount}
                  zeroReason={(dueNotifications.Count == 0 ? string.Join(", ", candidateDiagnostics.GroupBy(item => item.Reason).Select(group => $"{group.Key}={group.Count()}")) : "n/a")}
                """);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SnoozeAsync(string occurrenceKey, int minutes, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(occurrenceKey) || minutes <= 0)
        {
            return;
        }

        var current = now ?? DateTimeOffset.Now;
        await _gate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _maintenancePaused))
            {
                return;
            }

            var snoozed = await LoadSnoozeStateAsync();
            var snoozeUntil = current.AddMinutes(minutes);
            snoozed[occurrenceKey] = snoozeUntil.ToString("O");
            await SaveSnoozeStateAsync(snoozed);
            await MarkHistorySnoozedAsync(occurrenceKey, snoozeUntil);
            var candidateIndex = _diagnostics.Candidates.ToList().FindIndex(item => item.OccurrenceKey == occurrenceKey);
            if (candidateIndex >= 0)
            {
                var candidates = _diagnostics.Candidates.ToArray();
                candidates[candidateIndex] = candidates[candidateIndex] with
                {
                    CheckedAt = current,
                    IsDue = false,
                    SnoozedUntil = snoozeUntil,
                    Reason = "スヌーズ中"
                };
                _diagnostics = _diagnostics with { Candidates = candidates };
            }

            await SaveDiagnosticsAsync(force: true, current);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ReminderHistoryItem>> LoadHistoryAsync()
    {
        var json = await _repository.LoadSettingValueAsync(ReminderHistoryKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return (JsonSerializer.Deserialize<List<ReminderHistoryItem?>>(json) ?? [])
                .OfType<ReminderHistoryItem>()
                .ToArray();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            Debug.WriteLine(ex);
            return [];
        }
    }

    public async Task<bool> ShowTestNotificationAsync(CancellationToken cancellationToken = default)
    {
        return (await ShowTestNotificationDetailedAsync(null, cancellationToken)).Succeeded;
    }

    public async Task<bool> ShowTestNotificationAsync(IReminderNotifier? notifier, CancellationToken cancellationToken = default)
    {
        return (await ShowTestNotificationDetailedAsync(notifier, cancellationToken)).Succeeded;
    }

    public async Task<ReminderTestNotificationResult> ShowTestNotificationDetailedAsync(IReminderNotifier? notifier, CancellationToken cancellationToken = default)
    {
        var current = DateTimeOffset.Now;
        var notification = new ReminderNotification(
            $"test:{current.UtcTicks}",
            "test",
            "Test reminder",
            current.ToString("yyyy/MM/dd HH:mm"),
            current,
            current,
            current,
            GoogleCalendarDefaults.PrimaryCalendarId,
            IsTodoLike: false);

        var result = await TryDispatchNotificationAsync(notification, cancellationToken, notifier);
        await AddHistoryAsync(notification, current, null, result.Succeeded, result.DeliveryMethod, result.UsedMessageBoxFallback, result.MessageBoxRole, result.ToastVerified, result.ToastStatus, result.SoundStatus, result.SoundError, result.ErrorMessage);
        if (!result.Succeeded)
        {
            await _repository.SaveSettingValueAsync(ReminderLastErrorKey, $"{current:O} {result.ErrorMessage}");
        }

        return new ReminderTestNotificationResult(
            result.Succeeded,
            result.DeliveryMethod,
            result.UsedMessageBoxFallback,
            result.MessageBoxRole,
            result.ToastVerified,
            result.ToastStatus,
            result.SoundStatus,
            result.SoundError,
            result.ErrorMessage);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isRunning = false;
        UpdateRuntimeState(null);
        _timer.Dispose();
        _gate.Dispose();
    }

    private async Task<ReminderDeliveryResult> TryDispatchNotificationAsync(
        ReminderNotification notification,
        CancellationToken cancellationToken,
        IReminderNotifier? notifierOverride = null)
    {
        try
        {
            var dispatched = false;
            var notifier = notifierOverride ?? _notifier;
            if (notifier is not null)
            {
                await notifier.ShowAsync(notification, cancellationToken);
                dispatched = true;
            }

            if (ReminderTriggered is { } triggered)
            {
                foreach (var handler in triggered.GetInvocationList().Cast<Func<ReminderNotification, Task>>())
                {
                    await handler(notification);
                }
                dispatched = true;
            }

            var metadata = notifier as IReminderNotifierMetadata;
            return dispatched
                ? ReminderDeliveryResult.Success(metadata?.DeliveryMethodName ?? notifier?.GetType().Name ?? "ReminderTriggered", metadata?.UsedMessageBoxFallback ?? false, metadata?.MessageBoxRole ?? MessageBoxNotificationRole.None, metadata?.ToastVerified ?? false, metadata?.ToastStatus, metadata?.SoundStatus ?? ReminderSoundStatus.NotConfigured, metadata?.SoundError)
                : ReminderDeliveryResult.Failure("none", "No reminder notifier is configured.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            var notifier = notifierOverride ?? _notifier;
            var metadata = notifier as IReminderNotifierMetadata;
            return ReminderDeliveryResult.Failure(metadata?.DeliveryMethodName ?? notifier?.GetType().Name ?? "unknown", ex.Message, metadata?.UsedMessageBoxFallback ?? false, metadata?.MessageBoxRole ?? MessageBoxNotificationRole.None, metadata?.ToastVerified ?? false, metadata?.ToastStatus, metadata?.SoundStatus ?? ReminderSoundStatus.NotConfigured, metadata?.SoundError);
        }
    }

    private static bool ShouldConsiderReminder(CalendarEvent calendarEvent)
    {
        return !calendarEvent.IsDeleted
            && !calendarEvent.IsTodoDone
            && calendarEvent.ReminderMinutesBeforeStart is >= 0;
    }

    private static DateTimeOffset? TryGetSnoozedUntil(IReadOnlyDictionary<string, string> snoozed, string occurrenceKey)
    {
        return snoozed.TryGetValue(occurrenceKey, out var value) && DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static ReminderCandidateDiagnostic CreateCandidateDiagnostic(
        CalendarEvent calendarEvent,
        DateTimeOffset current,
        int? reminderMinutes,
        ReminderNotification? notification,
        bool isDue,
        bool isFired,
        DateTimeOffset? snoozedUntil,
        string reason)
    {
        return new ReminderCandidateDiagnostic(
            calendarEvent.Id, calendarEvent.Title, notification?.OccurrenceKey ?? "", reminderMinutes,
            calendarEvent.Start, notification?.RemindAt, current, isDue, isFired, snoozedUntil, reason,
            calendarEvent.GoogleReminderMetadata?.UseDefault,
            FormatMinutes(calendarEvent.GoogleReminderMetadata?.PopupMinutes),
            FormatMinutes(calendarEvent.GoogleReminderMetadata?.EmailMinutes),
            FormatDefaultReminders(calendarEvent.GoogleReminderMetadata),
            calendarEvent.GoogleReminderMetadata?.AdoptedReminderMinutes,
            GetReminderDifferenceText(calendarEvent));
    }

    private static string? GetGoogleReminderDiagnosticReason(CalendarEvent calendarEvent)
    {
        var metadata = calendarEvent.GoogleReminderMetadata;
        if (metadata is null || !metadata.HasGoogleReminder)
        {
            return null;
        }

        if (metadata.HasEmailOnly && metadata.AdoptedReminderMinutes is null)
        {
            return "Googleメール通知のみ（本ツールのポップアップ通知対象外）";
        }

        if (metadata.AdoptedReminderMinutes != calendarEvent.PrimaryAppReminderMinutesBeforeStart)
        {
            return "通知設定差分あり";
        }

        if (metadata.UseDefault == true)
        {
            return "Google既定通知あり";
        }

        return null;
    }

    private static string GetReminderDifferenceText(CalendarEvent calendarEvent)
    {
        var metadata = calendarEvent.GoogleReminderMetadata;
        if (metadata is null || !metadata.HasGoogleReminder)
        {
            return calendarEvent.IsAppReminderEnabled || calendarEvent.IsGoogleEmailReminderEnabled
                ? FormatSeparatedReminderText(calendarEvent)
                : "";
        }

        var appReminderMinutes = calendarEvent.EffectiveAppReminderMinutesBeforeStart;
        var googleEmailReminderMinutes = calendarEvent.EffectiveGoogleEmailReminderMinutesBeforeStart;
        if (appReminderMinutes.Count != 1 || googleEmailReminderMinutes.Count > 0)
        {
            return FormatSeparatedReminderText(calendarEvent);
        }

        if (metadata.HasEmailOnly && metadata.AdoptedReminderMinutes is null)
        {
            return "Googleメール通知のみ";
        }

        if (metadata.AdoptedReminderMethod is "email" or "default-email")
        {
            return metadata.AdoptedReminderMinutes == appReminderMinutes[0]
                ? FormatSeparatedReminderText(calendarEvent)
                : "Google email通知設定差分あり";
        }

        if (metadata.AdoptedReminderMinutes != appReminderMinutes[0])
        {
            return "通知設定差分あり";
        }

        return "";
    }

    private static string FormatSeparatedReminderText(CalendarEvent calendarEvent)
    {
        var appReminderMinutes = calendarEvent.EffectiveAppReminderMinutesBeforeStart;
        var googleEmailReminderMinutes = calendarEvent.EffectiveGoogleEmailReminderMinutesBeforeStart;
        var appText = appReminderMinutes.Count > 0
            ? $"アプリ内通知: {GoogleReminderDisplayFormatter.FormatReminderMinutes(appReminderMinutes)}"
            : "アプリ内通知: なし";
        var emailText = googleEmailReminderMinutes.Count > 0
            ? $"Googleメール通知: {GoogleReminderDisplayFormatter.FormatReminderMinutes(googleEmailReminderMinutes)}"
            : GoogleReminderDisplayFormatter.FormatEmailReminderText(calendarEvent.GoogleReminderMetadata);
        return $"{appText} / {emailText}";
    }

    private static string FormatDefaultReminders(GoogleReminderMetadata? metadata)
    {
        if (metadata is null)
        {
            return "";
        }

        var parts = new List<string>();
        var popup = FormatMinutes(metadata.DefaultPopupMinutes);
        if (!string.IsNullOrWhiteSpace(popup))
        {
            parts.Add($"popup {popup}");
        }

        var email = FormatMinutes(metadata.DefaultEmailMinutes);
        if (!string.IsNullOrWhiteSpace(email))
        {
            parts.Add($"email {email}");
        }

        if (metadata.UseDefault == true && parts.Count == 0)
        {
            return "使用（分数未取得）";
        }

        return string.Join(", ", parts);
    }

    private static string FormatMinutes(IEnumerable<int>? values)
    {
        var minutes = values?.Distinct().Order().ToArray() ?? [];
        return minutes.Length == 0 ? "" : string.Join(", ", minutes.Select(item => $"{item}分前"));
    }

    private async Task SaveDiagnosticsAsync(bool force = false, DateTimeOffset? current = null)
    {
        var savedAt = current ?? DateTimeOffset.Now;
        var signature = CreateDiagnosticsSignature(_diagnostics);
        var intervalElapsed = _lastDiagnosticsSavedAt is null
            || savedAt - _lastDiagnosticsSavedAt >= DiagnosticsPersistenceInterval;
        if (!force
            && !intervalElapsed
            && string.Equals(signature, _lastPersistedDiagnosticsSignature, StringComparison.Ordinal))
        {
            return;
        }

        await _repository.SaveSettingValueAsync(ReminderDiagnosticsKey, JsonSerializer.Serialize(_diagnostics));
        _lastDiagnosticsSavedAt = savedAt;
        _lastPersistedDiagnosticsSignature = signature;
    }

    private async Task SaveDiagnosticsSafelyAsync()
    {
        try
        {
            await SaveDiagnosticsAsync(force: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private static string CreateDiagnosticsSignature(ReminderMonitoringSnapshot snapshot)
    {
        var normalized = snapshot with
        {
            LastCheckAt = null,
            NextCheckAt = null,
            Candidates = snapshot.Candidates
                .Select(item => item with { CheckedAt = default })
                .ToArray()
        };
        return JsonSerializer.Serialize(normalized);
    }

    private void UpdateRuntimeState(DateTimeOffset? nextCheckAt)
    {
        _diagnostics = _diagnostics with { IsRunning = _isRunning, StartedAt = _startedAt, NextCheckAt = nextCheckAt };
    }

    private static ReminderNotification? CreateReminderNotification(CalendarEvent calendarEvent, DateTimeOffset now)
    {
        if (calendarEvent.ReminderMinutesBeforeStart is not int reminderMinutes)
        {
            return null;
        }

        return CreateReminderNotification(calendarEvent, now, reminderMinutes);
    }

    private static ReminderNotification CreateTodoReminderNotification(CalendarEvent calendarEvent)
    {
        var occurrenceStart = calendarEvent.OriginalStart ?? calendarEvent.Start;
        return new ReminderNotification(
            TodoReminderPolicy.BuildOccurrenceKey(calendarEvent),
            calendarEvent.Id,
            calendarEvent.Title,
            calendarEvent.DateDisplayText,
            TodoReminderPolicy.GetReminderTime(calendarEvent.Start.Date),
            calendarEvent.Start,
            occurrenceStart,
            calendarEvent.CalendarId,
            true);
    }

    private static ReminderNotification? CreateReminderNotification(CalendarEvent calendarEvent, DateTimeOffset now, int reminderMinutes)
    {
        var baseTime = calendarEvent.IsAllDay
            ? new DateTimeOffset(calendarEvent.Start.Date.AddHours(9), TimeZoneInfo.Local.GetUtcOffset(calendarEvent.Start.Date))
            : calendarEvent.Start;
        var remindAt = baseTime.AddMinutes(-reminderMinutes);
        var occurrenceStart = calendarEvent.OriginalStart ?? calendarEvent.Start;

        return new ReminderNotification(
            BuildOccurrenceKey(calendarEvent, reminderMinutes),
            calendarEvent.Id,
            calendarEvent.Title,
            calendarEvent.DateDisplayText,
            remindAt,
            calendarEvent.Start,
            occurrenceStart,
            calendarEvent.CalendarId,
            calendarEvent.IsTodoLike);
    }

    private static bool IsPastForReminder(CalendarEvent calendarEvent, DateTimeOffset current)
    {
        if (calendarEvent.IsAllDay)
        {
            return calendarEvent.End <= current;
        }

        return calendarEvent.Start < current;
    }

    private static bool IsDue(
        ReminderNotification notification,
        DateTimeOffset current,
        Dictionary<string, string> fired,
        Dictionary<string, string> snoozed)
    {
        if (snoozed.TryGetValue(notification.OccurrenceKey, out var snoozeValue)
            && DateTimeOffset.TryParse(snoozeValue, out var snoozeUntil))
        {
            return snoozeUntil <= current;
        }

        return notification.RemindAt <= current && !fired.ContainsKey(notification.OccurrenceKey);
    }

    private static string BuildOccurrenceKey(CalendarEvent calendarEvent, int reminderMinutes)
    {
        var anchor = calendarEvent.OriginalStart ?? calendarEvent.Start;
        var seriesKey = calendarEvent.RecurringParentId
            ?? calendarEvent.RecurringEventId
            ?? calendarEvent.Id;
        return $"{seriesKey}:{anchor.UtcTicks}:{reminderMinutes}";
    }

    private async Task<Dictionary<string, string>> LoadFiredStateAsync()
    {
        return await LoadStringDictionaryAsync(ReminderStateKey);
    }

    private async Task SaveFiredStateAsync(Dictionary<string, string> fired)
    {
        await SaveStringDictionaryAsync(ReminderStateKey, fired);
    }

    private async Task<Dictionary<string, string>> LoadSnoozeStateAsync()
    {
        return await LoadStringDictionaryAsync(ReminderSnoozeKey);
    }

    private async Task SaveSnoozeStateAsync(Dictionary<string, string> snoozed)
    {
        await SaveStringDictionaryAsync(ReminderSnoozeKey, snoozed);
    }

    private async Task<Dictionary<string, string>> LoadStringDictionaryAsync(string key)
    {
        var json = await _repository.LoadSettingValueAsync(key);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            Debug.WriteLine(ex);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private async Task SaveStringDictionaryAsync(string key, Dictionary<string, string> values)
    {
        var json = values.Count == 0 ? null : JsonSerializer.Serialize(values);
        await _repository.SaveSettingValueAsync(key, json);
    }

    private async Task AddHistoryAsync(
        ReminderNotification notification,
        DateTimeOffset notifiedAt,
        DateTimeOffset? snoozedUntil,
        bool deliverySucceeded,
        string? deliveryMethod,
        bool usedMessageBoxFallback,
        MessageBoxNotificationRole messageBoxRole,
        bool toastVerified,
        string? toastStatus,
        ReminderSoundStatus soundStatus,
        string? soundError,
        string? deliveryError)
    {
        var history = (await LoadHistoryAsync()).ToList();
        if (!deliverySucceeded
            && history.FirstOrDefault(item =>
                !item.DeliverySucceeded
                && string.Equals(item.OccurrenceKey, notification.OccurrenceKey, StringComparison.Ordinal)
                && notifiedAt - (item.LastFailedAt ?? item.NotifiedAt) <= FailureAggregationWindow) is { } existingFailure)
        {
            existingFailure.FailureCount = Math.Max(1, existingFailure.FailureCount) + 1;
            existingFailure.LastFailedAt = notifiedAt;
            existingFailure.DeliveryMethod = deliveryMethod;
            existingFailure.UsedMessageBoxFallback = usedMessageBoxFallback;
            existingFailure.MessageBoxRole = messageBoxRole;
            existingFailure.ToastVerified = toastVerified;
            existingFailure.ToastStatus = toastStatus;
            existingFailure.SoundStatus = soundStatus;
            existingFailure.SoundError = soundError;
            existingFailure.DeliveryError = deliveryError;
            existingFailure.SnoozedUntil = snoozedUntil;
            await SaveHistoryAsync(history.Take(MaxHistoryCount).ToList());
            return;
        }

        history.Insert(0, new ReminderHistoryItem
        {
            OccurrenceKey = notification.OccurrenceKey,
            EventId = notification.EventId,
            Title = notification.Title,
            DateDisplayText = notification.DateDisplayText,
            NotifiedAt = notifiedAt,
            RemindAt = notification.RemindAt,
            EventStart = notification.EventStart,
            OccurrenceStart = notification.OccurrenceStart,
            CalendarId = notification.CalendarId,
            IsTodoLike = notification.IsTodoLike,
            SnoozedUntil = snoozedUntil,
            DeliverySucceeded = deliverySucceeded,
            DeliveryMethod = deliveryMethod,
            UsedMessageBoxFallback = usedMessageBoxFallback,
            MessageBoxRole = messageBoxRole,
            ToastVerified = toastVerified,
            ToastStatus = toastStatus,
            SoundStatus = soundStatus,
            SoundError = soundError,
            DeliveryError = deliveryError,
            FailureCount = deliverySucceeded ? 0 : 1,
            LastFailedAt = deliverySucceeded ? null : notifiedAt
        });

        await SaveHistoryAsync(history.Take(MaxHistoryCount).ToList());
    }

    private async Task MarkHistorySnoozedAsync(string occurrenceKey, DateTimeOffset snoozedUntil)
    {
        var history = (await LoadHistoryAsync()).ToList();
        var item = history.FirstOrDefault(entry => string.Equals(entry.OccurrenceKey, occurrenceKey, StringComparison.Ordinal));
        if (item is not null)
        {
            item.SnoozedUntil = snoozedUntil;
            await SaveHistoryAsync(history.Take(MaxHistoryCount).ToList());
        }
    }

    private async Task SaveHistoryAsync(IReadOnlyList<ReminderHistoryItem> history)
    {
        var json = history.Count == 0 ? null : JsonSerializer.Serialize(history);
        await _repository.SaveSettingValueAsync(ReminderHistoryKey, json);
    }

    private static void PruneFiredState(Dictionary<string, string> fired, DateTimeOffset now)
    {
        var cutoff = now.AddDays(-60);
        foreach (var key in fired
                     .Where(pair => DateTimeOffset.TryParse(pair.Value, out var firedAt) && firedAt < cutoff)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            fired.Remove(key);
        }
    }

    private static void PruneSnoozeState(Dictionary<string, string> snoozed, DateTimeOffset cutoff)
    {
        foreach (var key in snoozed
                     .Where(pair => DateTimeOffset.TryParse(pair.Value, out var snoozedUntil) && snoozedUntil < cutoff)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            snoozed.Remove(key);
        }
    }
}

public sealed record ReminderNotification(
    string OccurrenceKey,
    string EventId,
    string Title,
    string DateDisplayText,
    DateTimeOffset RemindAt,
    DateTimeOffset EventStart,
    DateTimeOffset OccurrenceStart,
    string CalendarId,
    bool IsTodoLike);

public sealed record ReminderTestNotificationResult(
    bool Succeeded,
    string? DeliveryMethod,
    bool UsedMessageBoxFallback,
    MessageBoxNotificationRole MessageBoxRole,
    bool ToastVerified,
    string? ToastStatus,
    ReminderSoundStatus SoundStatus,
    string? SoundError,
    string? ErrorMessage);

internal sealed record ReminderDeliveryResult(
    bool Succeeded,
    string? DeliveryMethod,
    string? ErrorMessage,
    bool UsedMessageBoxFallback,
    MessageBoxNotificationRole MessageBoxRole,
    bool ToastVerified,
    string? ToastStatus,
    ReminderSoundStatus SoundStatus,
    string? SoundError)
{
    public static ReminderDeliveryResult Success(string? deliveryMethod, bool usedMessageBoxFallback, MessageBoxNotificationRole messageBoxRole, bool toastVerified, string? toastStatus, ReminderSoundStatus soundStatus, string? soundError) =>
        new(true, deliveryMethod, null, usedMessageBoxFallback, messageBoxRole, toastVerified, toastStatus, soundStatus, soundError);

    public static ReminderDeliveryResult Failure(
        string? deliveryMethod,
        string? errorMessage,
        bool usedMessageBoxFallback = false,
        MessageBoxNotificationRole messageBoxRole = MessageBoxNotificationRole.None,
        bool toastVerified = false,
        string? toastStatus = null,
        ReminderSoundStatus soundStatus = ReminderSoundStatus.NotConfigured,
        string? soundError = null) =>
        new(false, deliveryMethod, errorMessage, usedMessageBoxFallback, messageBoxRole, toastVerified, toastStatus, soundStatus, soundError);
}