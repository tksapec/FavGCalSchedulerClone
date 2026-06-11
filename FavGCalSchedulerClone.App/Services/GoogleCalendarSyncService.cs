using System.Diagnostics;
using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using Google;
using Google.Apis.Calendar.v3.Data;

namespace FavGCalSchedulerClone.App.Services;

public sealed class GoogleCalendarSyncService
{
    private const string EventColorPaletteSettingKey = "google-event-color-palette";
    private const string SyncLastResultKey = "sync:last-result";
    private const string SyncHistoryKey = "sync:history";
    private const string SyncLastFailuresKey = "sync:last-failures";
    private const int MaxSyncHistoryCount = 20;
    private readonly CalendarRepository _repository;
    private readonly IGoogleCalendarApi _googleCalendarApi;

    public GoogleCalendarSyncService(CalendarRepository repository)
        : this(repository, new GoogleCalendarApi())
    {
    }

    public GoogleCalendarSyncService(CalendarRepository repository, IGoogleCalendarApi googleCalendarApi)
    {
        _repository = repository;
        _googleCalendarApi = googleCalendarApi;
    }

    public async Task AuthorizeAsync(string clientJsonPath, CancellationToken cancellationToken = default)
    {
        _ = await _googleCalendarApi.CreateClientAsync(clientJsonPath, cancellationToken);
    }

    public async Task<IReadOnlyList<GoogleCalendarInfo>> ListCalendarsAsync(string clientJsonPath, CancellationToken cancellationToken = default)
    {
        var client = await _googleCalendarApi.CreateClientAsync(clientJsonPath, cancellationToken);
        return await client.ListCalendarsAsync(cancellationToken);
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
            var palette = await _googleCalendarApi.LoadEventColorPaletteAsync(cancellationToken);
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
        return _googleCalendarApi.ClearTokensAsync();
    }

    public async Task<SyncResult> SyncAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.Now;
        EnsureOAuthSettings(settings);

        var client = await _googleCalendarApi.CreateClientAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var pushed = 0;
        var pulled = 0;
        var skipped = 0;
        var conflicts = 0;
        var failed = 0;
        var deleted = 0;
        var recreated = 0;
        var failures = new List<SyncFailureDiagnostic>();

        foreach (var calendarId in await ResolveTargetCalendarIdsAsync(settings))
        {
            var push = await PushDirtyEventsAsync(client, calendarId, failures, localIds: null, cancellationToken);
            pushed += push.Pushed;
            failed += push.Failed;
            deleted += push.Deleted;
            recreated += push.Recreated;

            var pull = await PullRemoteEventsAsync(client, calendarId, settings.SyncConflictPolicy, failures, cancellationToken);
            pulled += pull.Pulled;
            skipped += pull.Skipped;
            conflicts += pull.Conflicts;
            failed += pull.Failed;
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
            $"送信 {pushed} / 取得 {pulled} / スキップ {skipped} / 競合 {conflicts} / 失敗 {failed} / 削除 {deleted} / 再作成 {recreated}");
        await SaveFailureDiagnosticsAsync(failures);
        await SaveSyncResultAsync(result, settings.EnableSyncDiagnostics);
        return result;
    }

    public async Task<SyncResult> SyncDirtyEventsAsync(AppSettings settings, IReadOnlySet<string> localIds, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.Now;
        EnsureOAuthSettings(settings);

        if (localIds.Count == 0)
        {
            return SyncResult.Empty("再同期対象がありません。");
        }

        var client = await _googleCalendarApi.CreateClientAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var failures = new List<SyncFailureDiagnostic>();
        var pushed = 0;
        var failed = 0;
        var deleted = 0;
        var recreated = 0;
        var dirtyEvents = await _repository.LoadDirtyEventsAsync();
        var targetCalendars = dirtyEvents
            .Where(item => localIds.Contains(item.Id))
            .Select(item => item.CalendarId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var calendarId in targetCalendars)
        {
            var push = await PushDirtyEventsAsync(client, calendarId, failures, localIds, cancellationToken);
            pushed += push.Pushed;
            failed += push.Failed;
            deleted += push.Deleted;
            recreated += push.Recreated;
        }

        var result = new SyncResult(
            pushed,
            0,
            0,
            0,
            failed,
            deleted,
            recreated,
            startedAt,
            DateTimeOffset.Now,
            $"選択対象の再同期: 送信 {pushed} / 失敗 {failed} / 削除 {deleted} / 再作成 {recreated}");
        await SaveFailureDiagnosticsAsync(failures);
        await SaveSyncResultAsync(result, settings.EnableSyncDiagnostics);
        return result;
    }

    public async Task<SyncResult> DiscardLocalChangesAsync(AppSettings settings, IReadOnlySet<string> localIds, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.Now;
        EnsureOAuthSettings(settings);

        var client = await _googleCalendarApi.CreateClientAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var restored = 0;
        var deleted = 0;
        var failed = 0;
        var failures = new List<SyncFailureDiagnostic>();

        foreach (var localId in localIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localEvent = await _repository.FindEventByIdAsync(localId);
            if (localEvent is null || !localEvent.IsDirty)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(localEvent.GoogleEventId))
            {
                if (!localEvent.IsDeleted)
                {
                    await _repository.HardDeleteEventAsync(localEvent.Id);
                    deleted++;
                }
                else
                {
                    await _repository.MarkSyncedAsync(localEvent);
                    restored++;
                }

                continue;
            }

            try
            {
                var googleEvent = await client.GetEventAsync(localEvent.CalendarId, localEvent.GoogleEventId, cancellationToken);
                if (string.Equals(googleEvent.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    await _repository.MarkSyncedAsync(localEvent);
                    restored++;
                    continue;
                }

                await _repository.UpsertSyncedEventAsync(GoogleEventMapper.FromGoogleEvent(googleEvent, localEvent.CalendarId));
                restored++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                failures.Add(CreateFailureDiagnostic(localEvent, "ローカル変更破棄", ex, "Googleから再取得できないため変更しませんでした。"));
            }
        }

        var result = new SyncResult(
            0,
            restored,
            0,
            0,
            failed,
            deleted,
            0,
            startedAt,
            DateTimeOffset.Now,
            $"ローカル変更破棄: 復元 {restored} / ローカル新規削除 {deleted} / 失敗 {failed}");
        await SaveFailureDiagnosticsAsync(failures);
        await SaveSyncResultAsync(result, settings.EnableSyncDiagnostics);
        return result;
    }

    public async Task<int> PullAsync(AppSettings settings, IEnumerable<string>? calendarIds = null, CancellationToken cancellationToken = default)
    {
        EnsureOAuthSettings(settings);

        var client = await _googleCalendarApi.CreateClientAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var targets = calendarIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (targets is null || targets.Length == 0)
        {
            targets = ResolveConfiguredTargetCalendarIds(settings).ToArray();
        }

        var pulled = 0;
        foreach (var calendarId in targets)
        {
            var failures = new List<SyncFailureDiagnostic>();
            var pull = await PullRemoteEventsAsync(client, calendarId, settings.SyncConflictPolicy, failures, cancellationToken);
            pulled += pull.Pulled;
            if (failures.Count > 0)
            {
                await SaveFailureDiagnosticsAsync(failures);
            }
        }

        return pulled;
    }

    public async Task<SyncPreview> PreviewAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        EnsureOAuthSettings(settings);

        var client = await _googleCalendarApi.CreateClientAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var pushItems = new List<SyncPreviewItem>();
        var pullItems = new List<SyncPreviewItem>();
        var deleteItems = new List<SyncPreviewItem>();
        var conflictItems = new List<SyncPreviewItem>();
        var errorItems = new List<SyncPreviewItem>();
        var calendars = new List<SyncCalendarDiagnostic>();
        var dirtyEvents = await _repository.LoadDirtyEventsAsync();

        foreach (var calendarId in ResolveTargetCalendarIds(settings, dirtyEvents))
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
                foreach (var googleEvent in await LoadRemoteChangesForPreviewAsync(client, calendarId, syncToken, cancellationToken))
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
        var failures = await LoadFailureDiagnosticsAsync();
        var dirtyEvents = await _repository.LoadDirtyEventsAsync();
        var calendars = new List<SyncCalendarDiagnostic>();
        foreach (var calendarId in ResolveTargetCalendarIds(settings, dirtyEvents))
        {
            var syncToken = await _repository.GetSyncTokenAsync(calendarId);
            calendars.Add(new SyncCalendarDiagnostic(
                calendarId,
                !string.IsNullOrWhiteSpace(syncToken),
                dirtyEvents.Count(item => item.CalendarId == calendarId)));
        }

        var dirtyItems = dirtyEvents
            .OrderBy(item => item.UpdatedAt)
            .Select(item => ToDirtyItem(item, failures.LastOrDefault(failure => string.Equals(failure.LocalId, item.Id, StringComparison.Ordinal))))
            .ToArray();
        return new SyncDiagnosticsSnapshot(history.FirstOrDefault(), history, calendars, dirtyEvents.Count, dirtyItems, failures);
    }

    public async Task<SyncResult> RecordFailedSyncAsync(string message, bool keepHistory)
    {
        var result = new SyncResult(0, 0, 0, 0, 1, 0, 0, DateTimeOffset.Now, DateTimeOffset.Now, message);
        await SaveFailureDiagnosticsAsync([]);
        await SaveSyncResultAsync(result, keepHistory);
        return result;
    }

    public Task ClearSyncDiagnosticsAsync()
    {
        return Task.WhenAll(
            _repository.SaveSettingValueAsync(SyncLastResultKey, null),
            _repository.SaveSettingValueAsync(SyncHistoryKey, null),
            _repository.SaveSettingValueAsync(SyncLastFailuresKey, null));
    }

    public async Task<IReadOnlySet<string>> FindExistingEventIdsAsync(
        string clientJsonPath,
        string calendarId,
        IEnumerable<string> eventIds,
        CancellationToken cancellationToken = default)
    {
        var client = await _googleCalendarApi.CreateClientAsync(clientJsonPath, cancellationToken);
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var eventId in eventIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var googleEvent = await client.GetEventAsync(calendarId, eventId, cancellationToken);
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

    private async Task<IReadOnlyList<string>> ResolveTargetCalendarIdsAsync(AppSettings settings)
    {
        return ResolveTargetCalendarIds(settings, await _repository.LoadDirtyEventsAsync());
    }

    private static IReadOnlyList<string> ResolveTargetCalendarIds(
        AppSettings settings,
        IEnumerable<CalendarEvent> dirtyEvents)
    {
        var ids = ResolveConfiguredTargetCalendarIds(settings).ToList();
        foreach (var calendarId in dirtyEvents
            .Select(item => item.CalendarId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal))
        {
            if (!ids.Contains(calendarId, StringComparer.Ordinal))
            {
                ids.Add(calendarId);
            }
        }

        return ids;
    }

    private static IReadOnlyList<string> ResolveConfiguredTargetCalendarIds(AppSettings settings)
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

    private async Task<SyncPushSummary> PushDirtyEventsAsync(
        IGoogleCalendarClient client,
        string calendarId,
        ICollection<SyncFailureDiagnostic> failures,
        IReadOnlySet<string>? localIds,
        CancellationToken cancellationToken)
    {
        var dirtyEvents = (await _repository.LoadDirtyEventsAsync())
            .Where(e => e.CalendarId == calendarId)
            .Where(e => localIds is null || localIds.Contains(e.Id))
            .ToArray();
        Debug.WriteLine($"PushDirtyEvents calendar={calendarId} count={dirtyEvents.Length}");
        foreach (var dirtyEvent in dirtyEvents)
        {
            Debug.WriteLine($"  localId={dirtyEvent.Id} fields={dirtyEvent.DirtyFields ?? "Unknown"}");
        }

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
            var operation = GetPushOperation(localEvent);
            var outcome = await TryPushEventAsync(localEvent, operation, failures, () => localEvent.IsRecurrenceException
                ? PushRecurrenceExceptionAsync(client, calendarId, localEvent, cancellationToken)
                : PushNormalEventAsync(client, calendarId, localEvent, cancellationToken));
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

    private static async Task<SyncPushOutcome> TryPushEventAsync(
        CalendarEvent localEvent,
        string operation,
        ICollection<SyncFailureDiagnostic> failures,
        Func<Task<SyncPushOutcome>> push)
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
            failures.Add(CreateFailureDiagnostic(localEvent, operation, ex));
            return SyncPushOutcome.Failed;
        }
    }

    private async Task<SyncPushOutcome> PushNormalEventAsync(IGoogleCalendarClient client, string calendarId, CalendarEvent localEvent, CancellationToken cancellationToken)
    {
        if (localEvent.IsDeleted)
        {
            if (!string.IsNullOrWhiteSpace(localEvent.GoogleEventId))
            {
                try
                {
                    await client.DeleteEventAsync(calendarId, localEvent.GoogleEventId, cancellationToken);
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
        Debug.WriteLine($"Push event Title={localEvent.Title} Description={localEvent.Description} Location={localEvent.Location} Start={localEvent.Start:O} End={localEvent.End:O} GoogleEventId={localEvent.GoogleEventId} CalendarId={calendarId} IsDirty={localEvent.IsDirty} DirtyFields={localEvent.DirtyFields}");
        if (string.IsNullOrWhiteSpace(localEvent.GoogleEventId))
        {
            var existingRemoteId = await FindExactRemoteMatchAsync(client, calendarId, localEvent, cancellationToken);
            if (!string.IsNullOrWhiteSpace(existingRemoteId))
            {
                await _repository.MarkSyncedAsync(localEvent, existingRemoteId);
                return SyncPushOutcome.Pushed;
            }

            var inserted = await client.InsertEventAsync(calendarId, googleEvent, cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, inserted.Id);
            return SyncPushOutcome.Pushed;
        }

        try
        {
            await client.UpdateEventAsync(calendarId, localEvent.GoogleEventId, googleEvent, cancellationToken);
            await _repository.MarkSyncedAsync(localEvent);
            Debug.WriteLine($"Push update succeeded and marked synced: {localEvent.Id}");
            return SyncPushOutcome.Pushed;
        }
        catch (GoogleApiException ex) when (IsNotFound(ex))
        {
            var inserted = await client.InsertEventAsync(calendarId, googleEvent, cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, inserted.Id);
            return SyncPushOutcome.RecreatedEvent;
        }
    }

    private async Task<SyncPushOutcome> PushRecurrenceExceptionAsync(IGoogleCalendarClient client, string calendarId, CalendarEvent localEvent, CancellationToken cancellationToken)
    {
        var recurringEventId = await ResolveRecurringEventIdAsync(localEvent);
        if (string.IsNullOrWhiteSpace(recurringEventId))
        {
            throw new InvalidOperationException("Recurring parent Google event ID could not be resolved.");
        }

        var remoteEventId = localEvent.GoogleEventId;
        if (string.IsNullOrWhiteSpace(remoteEventId))
        {
            remoteEventId = await ResolveRemoteOccurrenceIdAsync(client, calendarId, recurringEventId, localEvent, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(remoteEventId))
        {
            throw new InvalidOperationException("Recurring occurrence Google event ID could not be resolved.");
        }

        if (localEvent.IsDeleted)
        {
            try
            {
                await client.DeleteEventAsync(calendarId, remoteEventId, cancellationToken);
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
            var remoteEvent = await client.GetEventAsync(calendarId, remoteEventId, cancellationToken);
            remoteEvent.Summary = localEvent.Title;
            remoteEvent.Description = localEvent.Description;
            remoteEvent.Location = localEvent.Location;
            remoteEvent.ColorId = localEvent.ColorId;
            remoteEvent.Start = ToEventDateTime(localEvent.Start, localEvent.IsAllDay);
            remoteEvent.End = ToEventDateTime(localEvent.End, localEvent.IsAllDay);
            await client.UpdateEventAsync(calendarId, remoteEventId, remoteEvent, cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, remoteEventId);
            return SyncPushOutcome.Pushed;
        }
        catch (GoogleApiException ex) when (IsNotFound(ex))
        {
            var inserted = await client.InsertEventAsync(calendarId, GoogleEventMapper.ToGoogleEvent(localEvent), cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, inserted.Id);
            return SyncPushOutcome.RecreatedEvent;
        }
    }

    private static bool IsNotFound(GoogleApiException ex)
    {
        return ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound;
    }

    private static async Task<string?> FindExactRemoteMatchAsync(
        IGoogleCalendarClient client,
        string calendarId,
        CalendarEvent localEvent,
        CancellationToken cancellationToken)
    {
        string? pageToken = null;
        do
        {
            var page = await client.ListEventsAsync(
                new GoogleEventListRequest(
                    calendarId,
                    SyncToken: null,
                    pageToken,
                    localEvent.Start.AddDays(-1),
                    ShowDeleted: false,
                    SingleEvents: false,
                    MaxResults: 2500),
                cancellationToken);
            var match = page.Items.FirstOrDefault(item => IsExactRemoteMatch(item, localEvent));
            if (!string.IsNullOrWhiteSpace(match?.Id))
            {
                return match.Id;
            }

            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return null;
    }

    private static bool IsExactRemoteMatch(Event googleEvent, CalendarEvent localEvent)
    {
        if (string.Equals(googleEvent.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remote = GoogleEventMapper.FromGoogleEvent(googleEvent, localEvent.CalendarId);
        return string.Equals(remote.Title, localEvent.Title, StringComparison.Ordinal)
            && string.Equals(remote.Location ?? "", localEvent.Location ?? "", StringComparison.Ordinal)
            && remote.Start == localEvent.Start
            && remote.End == localEvent.End;
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
        IGoogleCalendarClient client,
        string calendarId,
        string recurringEventId,
        CalendarEvent localEvent,
        CancellationToken cancellationToken)
    {
        if (localEvent.OriginalStart is null)
        {
            return null;
        }

        var instances = await client.ListInstancesAsync(
            calendarId,
            recurringEventId,
            localEvent.OriginalStart.Value.AddDays(-1),
            localEvent.OriginalStart.Value.AddDays(1),
            showDeleted: true,
            maxResults: 20,
            cancellationToken);

        return instances
            .FirstOrDefault(item => MatchesOriginalStart(item, localEvent.OriginalStart.Value, localEvent.IsAllDay))
            ?.Id;
    }

    private async Task<SyncPullSummary> PullRemoteEventsAsync(
        IGoogleCalendarClient client,
        string calendarId,
        SyncConflictPolicy conflictPolicy,
        ICollection<SyncFailureDiagnostic> failures,
        CancellationToken cancellationToken)
    {
        var syncToken = await _repository.GetSyncTokenAsync(calendarId);
        var pulled = 0;
        var skipped = 0;
        var conflicts = 0;
        string? pageToken = null;

        try
        {
            do
            {
                var page = await client.ListEventsAsync(
                    new GoogleEventListRequest(
                        calendarId,
                        syncToken,
                        pageToken,
                        string.IsNullOrWhiteSpace(syncToken) ? DateTimeOffset.Now.AddYears(-5) : null,
                        ShowDeleted: true,
                        SingleEvents: false,
                        MaxResults: 2500),
                    cancellationToken);
                foreach (var googleEvent in page.Items)
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
            failures.Add(CreatePullFailureDiagnostic(calendarId, syncToken, pageToken, ex, "SyncTokenExpired", "410 Gone: sync token をリセットして再取得します。"));
            await _repository.SaveSyncTokenAsync(calendarId, null);
            var retry = await PullRemoteEventsAsync(client, calendarId, conflictPolicy, failures, cancellationToken);
            return new SyncPullSummary(pulled + retry.Pulled, skipped + retry.Skipped, conflicts + retry.Conflicts, retry.Failed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add(CreatePullFailureDiagnostic(calendarId, syncToken, pageToken, ex, "Pull", ex.Message));
            return new SyncPullSummary(pulled, skipped, conflicts, 1);
        }

        return new SyncPullSummary(pulled, skipped, conflicts, 0);
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
            TimeZone = GoogleCalendarTimeZone.LocalIanaId
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
            detail,
            calendarEvent.DirtyFields);
    }

    private static SyncDirtyItem ToDirtyItem(CalendarEvent calendarEvent, SyncFailureDiagnostic? failure)
    {
        return new SyncDirtyItem(
            calendarEvent.Id,
            calendarEvent.IsTodoLike ? "ToDo" : "予定",
            calendarEvent.CalendarId,
            calendarEvent.Start,
            string.IsNullOrWhiteSpace(calendarEvent.Title) ? "(no title)" : calendarEvent.Title,
            GetPushOperation(calendarEvent),
            calendarEvent.GoogleEventId,
            calendarEvent.UpdatedAt,
            failure?.FailureReason,
            failure?.ExceptionMessage ?? failure?.GoogleErrorMessage,
            calendarEvent.DirtyFields);
    }

    private static string GetPushOperation(CalendarEvent calendarEvent)
    {
        if (calendarEvent.IsDeleted)
        {
            return "削除";
        }

        return string.IsNullOrWhiteSpace(calendarEvent.GoogleEventId) ? "作成" : "更新";
    }

    private static SyncFailureDiagnostic CreateFailureDiagnostic(
        CalendarEvent calendarEvent,
        string operation,
        Exception? exception,
        string? reason = null)
    {
        var googleException = exception as GoogleApiException;
        return new SyncFailureDiagnostic(
            DateTimeOffset.Now,
            string.IsNullOrWhiteSpace(calendarEvent.Title) ? "(no title)" : calendarEvent.Title,
            calendarEvent.Start,
            calendarEvent.CalendarId,
            calendarEvent.Id,
            calendarEvent.GoogleEventId,
            operation,
            calendarEvent.IsTodoLike ? "ToDo" : "予定",
            reason ?? googleException?.Error?.Message ?? exception?.Message ?? "同期処理に失敗しました。",
            googleException?.HttpStatusCode.ToString(),
            googleException?.Error?.Message,
            exception?.Message);
    }

    private static SyncFailureDiagnostic CreatePullFailureDiagnostic(
        string calendarId,
        string? syncToken,
        string? pageToken,
        Exception exception,
        string category,
        string reason)
    {
        var googleException = exception as GoogleApiException;
        return new SyncFailureDiagnostic(
            DateTimeOffset.Now,
            "(pull)",
            DateTimeOffset.Now,
            calendarId,
            "",
            null,
            "取得",
            "Remote",
            reason,
            googleException?.HttpStatusCode.ToString(),
            googleException?.Error?.Message,
            exception.Message,
            "Pull",
            !string.IsNullOrWhiteSpace(syncToken),
            pageToken,
            category);
    }

    private static SyncDirtyItem ToDirtyItem(CalendarEvent calendarEvent)
    {
        return new SyncDirtyItem(
            calendarEvent.IsTodoLike ? "ToDo" : "予定",
            calendarEvent.CalendarId,
            calendarEvent.Start,
            string.IsNullOrWhiteSpace(calendarEvent.Title) ? "(no title)" : calendarEvent.Title,
            calendarEvent.IsDeleted ? "削除" : string.IsNullOrWhiteSpace(calendarEvent.GoogleEventId) ? "作成" : "更新",
            calendarEvent.GoogleEventId,
            calendarEvent.UpdatedAt);
    }

    private static async Task<IReadOnlyList<Event>> LoadRemoteChangesForPreviewAsync(
        IGoogleCalendarClient client,
        string calendarId,
        string? syncToken,
        CancellationToken cancellationToken)
    {
        var events = new List<Event>();
        string? pageToken = null;
        do
        {
            var page = await client.ListEventsAsync(
                new GoogleEventListRequest(
                    calendarId,
                    syncToken,
                    pageToken,
                    string.IsNullOrWhiteSpace(syncToken) ? DateTimeOffset.Now.AddYears(-5) : null,
                    ShowDeleted: true,
                    SingleEvents: false,
                    MaxResults: 2500),
                cancellationToken);
            events.AddRange(page.Items);
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

    private async Task SaveFailureDiagnosticsAsync(IReadOnlyCollection<SyncFailureDiagnostic> failures)
    {
        await _repository.SaveSettingValueAsync(
            SyncLastFailuresKey,
            failures.Count == 0 ? null : JsonSerializer.Serialize(failures));
    }

    private async Task<IReadOnlyList<SyncFailureDiagnostic>> LoadFailureDiagnosticsAsync()
    {
        var json = await _repository.LoadSettingValueAsync(SyncLastFailuresKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<SyncFailureDiagnostic>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
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

}

public enum GoogleNotFoundSyncAction
{
    MarkLocalSynced,
    RecreateRemote
}

internal sealed record SyncPushSummary(int Pushed, int Failed, int Deleted, int Recreated);
internal sealed record SyncPullSummary(int Pulled, int Skipped, int Conflicts, int Failed);
internal sealed record SyncPushOutcome(bool Success, bool Deleted, bool Recreated)
{
    public static SyncPushOutcome Pushed { get; } = new(true, false, false);
    public static SyncPushOutcome Failed { get; } = new(false, false, false);
    public static SyncPushOutcome DeletedEvent { get; } = new(true, true, false);
    public static SyncPushOutcome RecreatedEvent { get; } = new(true, false, true);
}
