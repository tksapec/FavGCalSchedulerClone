using System.Diagnostics;
using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace FavGCalSchedulerClone.App.Services;

public sealed class GoogleCalendarSyncService
{
    private const string EventColorPaletteSettingKey = "google-event-color-palette";
    private const string SyncLastResultKey = "sync:last-result";
    private const string SyncHistoryKey = "sync:history";
    private const int MaxSyncHistoryCount = 20;
    private readonly CalendarRepository _repository;

    public GoogleCalendarSyncService(CalendarRepository repository)
    {
        _repository = repository;
    }

    public async Task AuthorizeAsync(string clientJsonPath, CancellationToken cancellationToken = default)
    {
        _ = await CreateServiceAsync(clientJsonPath, cancellationToken);
    }

    public async Task<IReadOnlyList<GoogleCalendarInfo>> ListCalendarsAsync(string clientJsonPath, CancellationToken cancellationToken = default)
    {
        var service = await CreateServiceAsync(clientJsonPath, cancellationToken);
        var request = service.CalendarList.List();
        var page = await request.ExecuteAsync(cancellationToken);
        return (page.Items ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => new GoogleCalendarInfo(item.Id!, item.SummaryOverride ?? item.Summary ?? item.Id!))
            .OrderBy(item => item.Summary, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<string, EventDisplayColors>> LoadCachedEventColorPaletteAsync()
    {
        var serialized = await _repository.LoadSettingValueAsync(EventColorPaletteSettingKey);
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return TagService.DefaultEventColorPalette;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, EventDisplayColors>>(serialized)
                ?? TagService.DefaultEventColorPalette;
        }
        catch (JsonException)
        {
            return TagService.DefaultEventColorPalette;
        }
    }

    public async Task<IReadOnlyDictionary<string, EventDisplayColors>> RefreshEventColorPaletteAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var service = new CalendarService(new BaseClientService.Initializer
            {
                ApplicationName = "FavGCalSchedulerClone"
            });
            var colors = await service.Colors.Get().ExecuteAsync(cancellationToken);
            var palette = (colors.Event__ ?? new Dictionary<string, ColorDefinition>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Value.Background)
                               && !string.IsNullOrWhiteSpace(item.Value.Foreground))
                .ToDictionary(
                    item => item.Key,
                    item => new EventDisplayColors(item.Value.Background!, item.Value.Foreground!),
                    StringComparer.Ordinal);
            if (palette.Count > 0)
            {
                await _repository.SaveSettingValueAsync(EventColorPaletteSettingKey, JsonSerializer.Serialize(palette));
                return palette;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A palette lookup failure must not prevent local display or synchronization.
        }

        return await LoadCachedEventColorPaletteAsync();
    }

    public Task ClearTokensAsync()
    {
        var store = new ProtectedFileDataStore(AppPaths.TokenDirectory);
        return store.ClearAsync();
    }

    public async Task<SyncResult> SyncAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.Now;
        EnsureOAuthSettings(settings);

        var service = await CreateServiceAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var pushed = 0;
        var pulled = 0;
        var skipped = 0;
        var conflicts = 0;
        var failed = 0;
        var deleted = 0;
        var recreated = 0;

        foreach (var calendarId in ResolveTargetCalendarIds(settings))
        {
            var push = await PushDirtyEventsAsync(service, calendarId, cancellationToken);
            pushed += push.Pushed;
            failed += push.Failed;
            deleted += push.Deleted;
            recreated += push.Recreated;

            var pull = await PullRemoteEventsAsync(service, calendarId, settings.SyncConflictPolicy, cancellationToken);
            pulled += pull.Pulled;
            skipped += pull.Skipped;
            conflicts += pull.Conflicts;
        }

        var result = new SyncResult(
            pushed,
            pulled,
            skipped,
            conflicts,
            failed,
            deleted,
            recreated,
            startedAt,
            DateTimeOffset.Now,
            $"pushed={pushed}, pulled={pulled}, skipped={skipped}, conflicts={conflicts}, failed={failed}");
        await SaveSyncResultAsync(result, settings.EnableSyncDiagnostics);
        return result;
    }

    public async Task<int> PullAsync(AppSettings settings, IEnumerable<string>? calendarIds = null, CancellationToken cancellationToken = default)
    {
        EnsureOAuthSettings(settings);

        var service = await CreateServiceAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var targets = calendarIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (targets is null || targets.Length == 0)
        {
            targets = ResolveTargetCalendarIds(settings).ToArray();
        }

        var pulled = 0;
        foreach (var calendarId in targets)
        {
            pulled += (await PullRemoteEventsAsync(service, calendarId, settings.SyncConflictPolicy, cancellationToken)).Pulled;
        }

        return pulled;
    }

    public async Task<SyncPreview> PreviewAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        EnsureOAuthSettings(settings);

        var service = await CreateServiceAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var pushItems = new List<SyncPreviewItem>();
        var pullItems = new List<SyncPreviewItem>();
        var deleteItems = new List<SyncPreviewItem>();
        var conflictItems = new List<SyncPreviewItem>();
        var errorItems = new List<SyncPreviewItem>();
        var calendars = new List<SyncCalendarDiagnostic>();
        var dirtyEvents = await _repository.LoadDirtyEventsAsync();

        foreach (var calendarId in ResolveTargetCalendarIds(settings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var calendarDirty = dirtyEvents.Where(e => e.CalendarId == calendarId).OrderBy(e => e.UpdatedAt).ToArray();
            var syncToken = await _repository.GetSyncTokenAsync(calendarId);
            calendars.Add(new SyncCalendarDiagnostic(calendarId, !string.IsNullOrWhiteSpace(syncToken), calendarDirty.Length));

            foreach (var localEvent in calendarDirty)
            {
                var item = ToPreviewItem(calendarId, localEvent, localEvent.IsDeleted ? "delete" : "push", localEvent.IsDeleted ? "Googleから削除予定" : "Googleへ送信予定");
                if (localEvent.IsDeleted)
                {
                    deleteItems.Add(item);
                }
                else
                {
                    pushItems.Add(item);
                }
            }

            try
            {
                foreach (var googleEvent in await LoadRemoteChangesForPreviewAsync(service, calendarId, syncToken, cancellationToken))
                {
                    var remoteEvent = GoogleEventMapper.FromGoogleEvent(googleEvent, calendarId);
                    var item = ToPreviewItem(calendarId, remoteEvent, "pull", "Googleから取得予定");
                    var local = await _repository.FindEventByGoogleEventIdAsync(calendarId, googleEvent.Id);
                    if (local?.IsDirty == true)
                    {
                        conflictItems.Add(item with { Detail = $"ローカル未同期変更とGoogle変更が競合: {settings.SyncConflictPolicy}" });
                    }
                    else if (remoteEvent.IsDeleted)
                    {
                        deleteItems.Add(item with { Kind = "remote-delete", Detail = "Google側削除を反映予定" });
                    }
                    else
                    {
                        pullItems.Add(item);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errorItems.Add(new SyncPreviewItem(calendarId, null, null, "Preview error", null, "error", ex.Message));
            }
        }

        return new SyncPreview(DateTimeOffset.Now, pushItems, pullItems, deleteItems, conflictItems, errorItems, calendars);
    }

    public async Task<SyncDiagnosticsSnapshot> LoadDiagnosticsAsync(AppSettings settings)
    {
        var history = await LoadSyncHistoryAsync();
        var dirtyEvents = await _repository.LoadDirtyEventsAsync();
        var calendars = new List<SyncCalendarDiagnostic>();
        foreach (var calendarId in ResolveTargetCalendarIds(settings))
        {
            var syncToken = await _repository.GetSyncTokenAsync(calendarId);
            calendars.Add(new SyncCalendarDiagnostic(
                calendarId,
                !string.IsNullOrWhiteSpace(syncToken),
                dirtyEvents.Count(item => item.CalendarId == calendarId)));
        }

        return new SyncDiagnosticsSnapshot(history.FirstOrDefault(), history, calendars, dirtyEvents.Count);
    }

    public async Task<SyncResult> RecordFailedSyncAsync(string message, bool keepHistory)
    {
        var result = new SyncResult(0, 0, 0, 0, 1, 0, 0, DateTimeOffset.Now, DateTimeOffset.Now, message);
        await SaveSyncResultAsync(result, keepHistory);
        return result;
    }

    public Task ClearSyncDiagnosticsAsync()
    {
        return Task.WhenAll(
            _repository.SaveSettingValueAsync(SyncLastResultKey, null),
            _repository.SaveSettingValueAsync(SyncHistoryKey, null));
    }

    public async Task<IReadOnlySet<string>> FindExistingEventIdsAsync(
        string clientJsonPath,
        string calendarId,
        IEnumerable<string> eventIds,
        CancellationToken cancellationToken = default)
    {
        var service = await CreateServiceAsync(clientJsonPath, cancellationToken);
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var eventId in eventIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var googleEvent = await service.Events.Get(calendarId, eventId).ExecuteAsync(cancellationToken);
                if (!string.Equals(googleEvent.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    existing.Add(eventId);
                }
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
            }
        }

        return existing;
    }

    internal static GoogleNotFoundSyncAction ResolveNotFoundAction(CalendarEvent localEvent)
    {
        return localEvent.IsDeleted
            ? GoogleNotFoundSyncAction.MarkLocalSynced
            : GoogleNotFoundSyncAction.RecreateRemote;
    }

    internal static bool ShouldApplyRemoteChange(CalendarEvent? existingLocal, SyncConflictPolicy conflictPolicy)
    {
        return existingLocal?.IsDirty != true || conflictPolicy == SyncConflictPolicy.PreferGoogle;
    }

    private static void EnsureOAuthSettings(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OAuthClientJsonPath) || !File.Exists(settings.OAuthClientJsonPath))
        {
            throw new InvalidOperationException("OAuth client JSONを設定してください。");
        }
    }

    private static IReadOnlyList<string> ResolveTargetCalendarIds(AppSettings settings)
    {
        var ids = settings.VisibleCalendarIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length > 0)
        {
            return ids;
        }

        return [string.IsNullOrWhiteSpace(settings.ActiveCalendarId) ? GoogleCalendarDefaults.PrimaryCalendarId : settings.ActiveCalendarId];
    }

    private async Task<SyncPushSummary> PushDirtyEventsAsync(CalendarService service, string calendarId, CancellationToken cancellationToken)
    {
        var dirtyEvents = (await _repository.LoadDirtyEventsAsync())
            .Where(e => e.CalendarId == calendarId)
            .ToArray();

        var ordered = dirtyEvents
            .OrderByDescending(item => item.IsRecurringMaster)
            .ThenBy(item => item.IsRecurrenceException)
            .ThenBy(item => item.UpdatedAt)
            .ToArray();
        var pushed = 0;
        var failed = 0;
        var deleted = 0;
        var recreated = 0;

        foreach (var localEvent in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await TryPushEventAsync(() => localEvent.IsRecurrenceException
                ? PushRecurrenceExceptionAsync(service, calendarId, localEvent, cancellationToken)
                : PushNormalEventAsync(service, calendarId, localEvent, cancellationToken));
            if (!outcome.Success)
            {
                failed++;
                continue;
            }

            pushed++;
            deleted += outcome.Deleted ? 1 : 0;
            recreated += outcome.Recreated ? 1 : 0;
        }

        return new SyncPushSummary(pushed, failed, deleted, recreated);
    }

    private static async Task<SyncPushOutcome> TryPushEventAsync(Func<Task<SyncPushOutcome>> push)
    {
        try
        {
            return await push();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return SyncPushOutcome.Failed;
        }
    }

    private async Task<SyncPushOutcome> PushNormalEventAsync(CalendarService service, string calendarId, CalendarEvent localEvent, CancellationToken cancellationToken)
    {
        if (localEvent.IsDeleted)
        {
            if (!string.IsNullOrWhiteSpace(localEvent.GoogleEventId))
            {
                try
                {
                    await service.Events.Delete(calendarId, localEvent.GoogleEventId).ExecuteAsync(cancellationToken);
                }
                catch (GoogleApiException ex) when (IsNotFound(ex))
                {
                    await _repository.MarkSyncedAsync(localEvent);
                    return SyncPushOutcome.DeletedEvent;
                }
            }

            await _repository.MarkSyncedAsync(localEvent);
            return SyncPushOutcome.DeletedEvent;
        }

        var googleEvent = GoogleEventMapper.ToGoogleEvent(localEvent);
        if (string.IsNullOrWhiteSpace(localEvent.GoogleEventId))
        {
            var inserted = await service.Events.Insert(googleEvent, calendarId).ExecuteAsync(cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, inserted.Id);
            return SyncPushOutcome.Pushed;
        }

        try
        {
            await service.Events.Update(googleEvent, calendarId, localEvent.GoogleEventId).ExecuteAsync(cancellationToken);
            await _repository.MarkSyncedAsync(localEvent);
            return SyncPushOutcome.Pushed;
        }
        catch (GoogleApiException ex) when (IsNotFound(ex))
        {
            var inserted = await service.Events.Insert(googleEvent, calendarId).ExecuteAsync(cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, inserted.Id);
            return SyncPushOutcome.RecreatedEvent;
        }
    }

    private async Task<SyncPushOutcome> PushRecurrenceExceptionAsync(CalendarService service, string calendarId, CalendarEvent localEvent, CancellationToken cancellationToken)
    {
        var recurringEventId = await ResolveRecurringEventIdAsync(localEvent);
        if (string.IsNullOrWhiteSpace(recurringEventId))
        {
            return SyncPushOutcome.Failed;
        }

        var remoteEventId = localEvent.GoogleEventId;
        if (string.IsNullOrWhiteSpace(remoteEventId))
        {
            remoteEventId = await ResolveRemoteOccurrenceIdAsync(service, calendarId, recurringEventId, localEvent, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(remoteEventId))
        {
            return SyncPushOutcome.Failed;
        }

        if (localEvent.IsDeleted)
        {
            try
            {
                await service.Events.Delete(calendarId, remoteEventId).ExecuteAsync(cancellationToken);
            }
            catch (GoogleApiException ex) when (IsNotFound(ex))
            {
                await _repository.MarkSyncedAsync(localEvent, remoteEventId);
                return SyncPushOutcome.DeletedEvent;
            }

            await _repository.MarkSyncedAsync(localEvent, remoteEventId);
            return SyncPushOutcome.DeletedEvent;
        }

        try
        {
            var remoteEvent = await service.Events.Get(calendarId, remoteEventId).ExecuteAsync(cancellationToken);
            remoteEvent.Summary = localEvent.Title;
            remoteEvent.Description = localEvent.Description;
            remoteEvent.Location = localEvent.Location;
            remoteEvent.ColorId = localEvent.ColorId;
            remoteEvent.Start = ToEventDateTime(localEvent.Start, localEvent.IsAllDay);
            remoteEvent.End = ToEventDateTime(localEvent.End, localEvent.IsAllDay);
            await service.Events.Update(remoteEvent, calendarId, remoteEventId).ExecuteAsync(cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, remoteEventId);
            return SyncPushOutcome.Pushed;
        }
        catch (GoogleApiException ex) when (IsNotFound(ex))
        {
            var inserted = await service.Events.Insert(GoogleEventMapper.ToGoogleEvent(localEvent), calendarId).ExecuteAsync(cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, inserted.Id);
            return SyncPushOutcome.RecreatedEvent;
        }
    }

    private static bool IsNotFound(GoogleApiException ex)
    {
        return ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound;
    }

    private async Task<string?> ResolveRecurringEventIdAsync(CalendarEvent localEvent)
    {
        if (!string.IsNullOrWhiteSpace(localEvent.RecurringEventId))
        {
            return localEvent.RecurringEventId;
        }

        var parent = await _repository.FindMasterByIdAsync(localEvent.RecurringParentId);
        return parent?.GoogleEventId;
    }

    private static async Task<string?> ResolveRemoteOccurrenceIdAsync(
        CalendarService service,
        string calendarId,
        string recurringEventId,
        CalendarEvent localEvent,
        CancellationToken cancellationToken)
    {
        if (localEvent.OriginalStart is null)
        {
            return null;
        }

        var request = service.Events.Instances(calendarId, recurringEventId);
        request.TimeMinDateTimeOffset = localEvent.OriginalStart.Value.AddDays(-1);
        request.TimeMaxDateTimeOffset = localEvent.OriginalStart.Value.AddDays(1);
        request.ShowDeleted = true;
        request.MaxResults = 20;
        var page = await request.ExecuteAsync(cancellationToken);

        return (page.Items ?? [])
            .FirstOrDefault(item => MatchesOriginalStart(item, localEvent.OriginalStart.Value, localEvent.IsAllDay))
            ?.Id;
    }

    private async Task<SyncPullSummary> PullRemoteEventsAsync(CalendarService service, string calendarId, SyncConflictPolicy conflictPolicy, CancellationToken cancellationToken)
    {
        var syncToken = await _repository.GetSyncTokenAsync(calendarId);
        var pulled = 0;
        var skipped = 0;
        var conflicts = 0;

        try
        {
            string? pageToken = null;
            do
            {
                var request = service.Events.List(calendarId);
                request.ShowDeleted = true;
                request.SingleEvents = false;
                request.MaxResults = 2500;
                request.PageToken = pageToken;
                if (string.IsNullOrWhiteSpace(syncToken))
                {
                    request.TimeMinDateTimeOffset = DateTimeOffset.Now.AddYears(-5);
                }
                else
                {
                    request.SyncToken = syncToken;
                }

                var page = await request.ExecuteAsync(cancellationToken);
                foreach (var googleEvent in page.Items ?? [])
                {
                    var localEvent = await _repository.FindEventByGoogleEventIdAsync(calendarId, googleEvent.Id);
                    if (!ShouldApplyRemoteChange(localEvent, conflictPolicy))
                    {
                        conflicts++;
                        skipped++;
                        continue;
                    }

                    await _repository.UpsertSyncedEventAsync(GoogleEventMapper.FromGoogleEvent(googleEvent, calendarId));
                    pulled++;
                }

                if (string.IsNullOrWhiteSpace(page.NextPageToken))
                {
                    await _repository.SaveSyncTokenAsync(calendarId, page.NextSyncToken);
                    break;
                }

                pageToken = page.NextPageToken;
            }
            while (true);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Gone)
        {
            await _repository.SaveSyncTokenAsync(calendarId, null);
            return await PullRemoteEventsAsync(service, calendarId, conflictPolicy, cancellationToken);
        }

        return new SyncPullSummary(pulled, skipped, conflicts);
    }

    private static bool MatchesOriginalStart(Event googleEvent, DateTimeOffset expected, bool isAllDay)
    {
        if (googleEvent.OriginalStartTime is null)
        {
            return false;
        }

        if (isAllDay && DateTime.TryParse(googleEvent.OriginalStartTime.Date, out var date))
        {
            return date.Date == expected.Date;
        }

        return googleEvent.OriginalStartTime.DateTimeDateTimeOffset?.UtcDateTime == expected.UtcDateTime;
    }

    private static EventDateTime ToEventDateTime(DateTimeOffset value, bool isAllDay)
    {
        if (isAllDay)
        {
            return new EventDateTime { Date = value.Date.ToString("yyyy-MM-dd") };
        }

        return new EventDateTime
        {
            DateTimeDateTimeOffset = value,
            TimeZone = TimeZoneInfo.Local.Id
        };
    }

    private static SyncPreviewItem ToPreviewItem(string calendarId, CalendarEvent calendarEvent, string kind, string detail)
    {
        return new SyncPreviewItem(
            calendarId,
            calendarEvent.Id,
            calendarEvent.GoogleEventId,
            string.IsNullOrWhiteSpace(calendarEvent.Title) ? "(no title)" : calendarEvent.Title,
            calendarEvent.Start,
            kind,
            detail);
    }

    private static async Task<IReadOnlyList<Event>> LoadRemoteChangesForPreviewAsync(
        CalendarService service,
        string calendarId,
        string? syncToken,
        CancellationToken cancellationToken)
    {
        var events = new List<Event>();
        string? pageToken = null;
        do
        {
            var request = service.Events.List(calendarId);
            request.ShowDeleted = true;
            request.SingleEvents = false;
            request.MaxResults = 2500;
            request.PageToken = pageToken;
            if (string.IsNullOrWhiteSpace(syncToken))
            {
                request.TimeMinDateTimeOffset = DateTimeOffset.Now.AddYears(-5);
            }
            else
            {
                request.SyncToken = syncToken;
            }

            var page = await request.ExecuteAsync(cancellationToken);
            events.AddRange(page.Items ?? []);
            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return events;
    }

    private async Task SaveSyncResultAsync(SyncResult result, bool keepHistory)
    {
        await _repository.SaveSettingValueAsync(SyncLastResultKey, JsonSerializer.Serialize(result));
        if (!keepHistory)
        {
            return;
        }

        var history = (await LoadStoredSyncHistoryAsync()).ToList();
        history.Insert(0, result);
        await _repository.SaveSettingValueAsync(SyncHistoryKey, JsonSerializer.Serialize(history.Take(MaxSyncHistoryCount)));
    }

    private async Task<IReadOnlyList<SyncResult>> LoadSyncHistoryAsync()
    {
        var lastJson = await _repository.LoadSettingValueAsync(SyncLastResultKey);
        var history = (await LoadStoredSyncHistoryAsync()).ToList();
        var last = DeserializeSyncResult(lastJson);
        if (last is not null && history.All(item => item.StartedAt != last.StartedAt))
        {
            history.Insert(0, last);
        }

        return history
            .OrderByDescending(item => item.StartedAt)
            .Take(MaxSyncHistoryCount)
            .ToArray();
    }

    private async Task<IReadOnlyList<SyncResult>> LoadStoredSyncHistoryAsync()
    {
        return DeserializeSyncResults(await _repository.LoadSettingValueAsync(SyncHistoryKey));
    }

    private static IReadOnlyList<SyncResult> DeserializeSyncResults(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<SyncResult>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static SyncResult? DeserializeSyncResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SyncResult>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<CalendarService> CreateServiceAsync(string clientJsonPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(clientJsonPath);
        var secrets = GoogleClientSecrets.FromStream(stream);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            [GoogleCalendarDefaults.CalendarEventsScope],
            "personal-user",
            cancellationToken,
            new ProtectedFileDataStore(AppPaths.TokenDirectory));

        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "FavGCalSchedulerClone"
        });
    }
}

public enum GoogleNotFoundSyncAction
{
    MarkLocalSynced,
    RecreateRemote
}

internal sealed record SyncPushSummary(int Pushed, int Failed, int Deleted, int Recreated);
internal sealed record SyncPullSummary(int Pulled, int Skipped, int Conflicts);
internal sealed record SyncPushOutcome(bool Success, bool Deleted, bool Recreated)
{
    public static SyncPushOutcome Pushed { get; } = new(true, false, false);
    public static SyncPushOutcome Failed { get; } = new(false, false, false);
    public static SyncPushOutcome DeletedEvent { get; } = new(true, true, false);
    public static SyncPushOutcome RecreatedEvent { get; } = new(true, false, true);
}
