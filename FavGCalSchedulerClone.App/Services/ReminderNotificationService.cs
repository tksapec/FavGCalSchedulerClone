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
    private const int MaxHistoryCount = 50;
    private static readonly TimeSpan FailureAggregationWindow = TimeSpan.FromMinutes(5);
    private readonly CalendarRepository _repository;
    private readonly Timer _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReminderNotifier? _notifier;
    private bool _disposed;

    public ReminderNotificationService(CalendarRepository repository, IReminderNotifier? notifier = null)
    {
        _repository = repository;
        _notifier = notifier;
        _timer = new Timer(_ => _ = CheckDueRemindersAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Func<ReminderNotification, Task>? ReminderTriggered;

    public void SetNotifier(IReminderNotifier notifier)
    {
        _notifier = notifier;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await CheckDueRemindersAsync(DateTimeOffset.Now, cancellationToken);
        _timer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public async Task CheckDueRemindersAsync(DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await CheckDueRemindersCoreAsync(now, cancellationToken);
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
            await _repository.SaveSettingValueAsync(ReminderLastErrorKey, $"{DateTimeOffset.Now:O} {ex}");
        }
    }

    private async Task CheckDueRemindersCoreAsync(DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        var current = now ?? DateTimeOffset.Now;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var fired = await LoadFiredStateAsync();
            var snoozed = await LoadSnoozeStateAsync();
            PruneFiredState(fired, current);
            PruneSnoozeState(snoozed, current.AddDays(-7));

            var windowStart = current.AddDays(-1);
            var windowEnd = current.AddDays(30);
            var storedEvents = await _repository.LoadEventsAsync(windowStart, windowEnd, includeDeleted: false);
            var expandedEvents = RecurrenceExpansionService.ExpandForRange(storedEvents, windowStart, windowEnd);
            var dueNotifications = expandedEvents
                .Where(ShouldConsiderReminder)
                .Select(item => CreateReminderNotification(item, current))
                .Where(item => item is not null)
                .Cast<ReminderNotification>()
                .Where(item => IsDue(item, current, fired, snoozed))
                .OrderBy(item => item.RemindAt)
                .ThenBy(item => item.EventStart)
                .ToArray();

            foreach (var notification in dueNotifications)
            {
                var result = await TryDispatchNotificationAsync(notification, cancellationToken);
                if (result.Succeeded)
                {
                    fired[notification.OccurrenceKey] = current.ToString("O");
                    snoozed.Remove(notification.OccurrenceKey);
                    await AddHistoryAsync(notification, current, null, deliverySucceeded: true, deliveryMethod: result.DeliveryMethod, result.UsedMessageBoxFallback, result.MessageBoxRole, result.ToastVerified, result.ToastStatus, result.SoundStatus, result.SoundError, deliveryError: null);
                }
                else
                {
                    await AddHistoryAsync(notification, current, null, deliverySucceeded: false, deliveryMethod: result.DeliveryMethod, result.UsedMessageBoxFallback, result.MessageBoxRole, result.ToastVerified, result.ToastStatus, result.SoundStatus, result.SoundError, deliveryError: result.ErrorMessage);
                    await _repository.SaveSettingValueAsync(ReminderLastErrorKey, $"{current:O} {result.ErrorMessage}");
                }
            }

            await SaveFiredStateAsync(fired);
            await SaveSnoozeStateAsync(snoozed);
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
            var snoozed = await LoadSnoozeStateAsync();
            var snoozeUntil = current.AddMinutes(minutes);
            snoozed[occurrenceKey] = snoozeUntil.ToString("O");
            await SaveSnoozeStateAsync(snoozed);
            await MarkHistorySnoozedAsync(occurrenceKey, snoozeUntil);
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
            return JsonSerializer.Deserialize<List<ReminderHistoryItem>>(json) ?? [];
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

            if (ReminderTriggered is not null)
            {
                await ReminderTriggered.Invoke(notification);
                dispatched = true;
            }

            var metadata = notifier as IReminderNotifierMetadata;
            return dispatched
                ? ReminderDeliveryResult.Success(metadata?.DeliveryMethodName ?? notifier?.GetType().Name ?? "ReminderTriggered", metadata?.UsedMessageBoxFallback ?? false, metadata?.MessageBoxRole ?? MessageBoxNotificationRole.None, metadata?.ToastVerified ?? false, metadata?.ToastStatus, metadata?.SoundStatus ?? ReminderSoundStatus.NotConfigured, metadata?.SoundError)
                : ReminderDeliveryResult.Failure("none", "No reminder notifier is configured.");
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

    private static ReminderNotification? CreateReminderNotification(CalendarEvent calendarEvent, DateTimeOffset now)
    {
        if (calendarEvent.ReminderMinutesBeforeStart is not int reminderMinutes)
        {
            return null;
        }

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
