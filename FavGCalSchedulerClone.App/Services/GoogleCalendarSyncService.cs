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
    private readonly IAppLogger? _logger;

    public GoogleCalendarSyncService(CalendarRepository repository)
        : this(repository, new GoogleCalendarApi())
    {
    }

    public GoogleCalendarSyncService(CalendarRepository repository, IGoogleCalendarApi googleCalendarApi, IAppLogger? logger = null)
    {
        _repository = repository;
        _googleCalendarApi = googleCalendarApi;
        _logger = logger;
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

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<GoogleReminderOverride>>> LoadCalendarReminderDefaultsAsync(
        IGoogleCalendarClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await client.ListCalendarsAsync(cancellationToken))
                .ToDictionary(
                    item => item.Id,
                    item => item.DefaultReminders ?? [],
                    StringComparer.Ordinal);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return new Dictionary<string, IReadOnlyList<GoogleReminderOverride>>(StringComparer.Ordinal);
        }
    }

    private static IReadOnlyList<GoogleReminderOverride>? GetDefaultReminders(
        IReadOnlyDictionary<string, IReadOnlyList<GoogleReminderOverride>> reminderDefaults,
        string calendarId)
    {
        return reminderDefaults.TryGetValue(calendarId, out var reminders) ? reminders : null;
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

    public Task<SyncResult> SyncAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        return SyncAsync(settings, refreshReminderMetadataAfterSync: false, cancellationToken);
    }

    public async Task<SyncResult> SyncAsync(
        AppSettings settings,
        bool refreshReminderMetadataAfterSync,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.Now;
        EnsureOAuthSettings(settings);

        var client = await _googleCalendarApi.CreateClientAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var reminderDefaults = await LoadCalendarReminderDefaultsAsync(client, cancellationToken);
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
            var syncToken = await _repository.GetSyncTokenAsync(calendarId);
            var dirtyEvents = (await _repository.LoadDirtyEventsAsync())
                .Where(item => string.Equals(item.CalendarId, calendarId, StringComparison.Ordinal)).ToArray();
            var resolved = await ResolveSyncPlanAsync(client, calendarId, dirtyEvents, syncToken,
                settings.SyncConflictPolicy, isPreview: false, failures, cancellationToken);
            if (!resolved.Delta.Success)
            {
                failed++;
                continue;
            }

            var execution = await ExecuteSyncPlanAsync(
                client,
                calendarId,
                resolved.Plan,
                failures,
                reminderDefaults,
                settings.AdoptGoogleEmailRemindersAsLocalNotifications,
                cancellationToken);
            pushed += execution.Pushed;
            pulled += execution.Pulled;
            skipped += execution.Skipped;
            conflicts += execution.Conflicts;
            failed += resolved.FailedLocalIds.Count + execution.Failed;
            deleted += execution.Deleted;
            recreated += execution.Recreated;

            if (resolved.FailedLocalIds.Count == 0 && execution.Failed == 0 && execution.Skipped == 0)
            {
                await _repository.SaveSyncTokenAsync(calendarId, resolved.Delta.NextSyncToken);
            }
        }

        var reminderRefreshMessage = string.Empty;
        if (refreshReminderMetadataAfterSync)
        {
            try
            {
                var now = DateTimeOffset.Now;
                var reminderRefresh = await RefreshRemoteEventsAndReminderMetadataCoreAsync(
                    client,
                    reminderDefaults,
                    settings,
                    now.AddDays(-1),
                    now.AddDays(30),
                    cancellationToken);
                reminderRefreshMessage = $" / Google通知設定再取得: {reminderRefresh.TotalAffected} 件";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Debug.WriteLine(ex);
                reminderRefreshMessage = $" / Google通知設定再取得失敗: {ex.Message}";
                failures.Add(new SyncFailureDiagnostic(
                    DateTimeOffset.Now,
                    "Google通知設定再取得",
                    DateTimeOffset.Now,
                    string.Empty,
                    string.Empty,
                    null,
                    "Google通知設定再取得",
                    "ReminderMetadataRefresh",
                    ex.Message,
                    null,
                    null,
                    ex.Message,
                    Direction: "Pull",
                    FailureCategory: "ReminderMetadataRefresh"));
            }
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
        if (!string.IsNullOrWhiteSpace(reminderRefreshMessage))
        {
            result = result with { Message = result.Message + reminderRefreshMessage };
        }

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
        var reminderDefaults = await LoadCalendarReminderDefaultsAsync(client, cancellationToken);
        var failures = new List<SyncFailureDiagnostic>();
        var pushed = 0;
        var pulled = 0;
        var skipped = 0;
        var conflicts = 0;
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
            var selectedDirty = dirtyEvents
                .Where(item => item.CalendarId == calendarId && localIds.Contains(item.Id))
                .ToArray();
            var remoteLookup = await LoadRemoteEventsForDirtyAsync(client, calendarId, selectedDirty, failures, cancellationToken);
            var executableDirty = selectedDirty
                .Where(item => !remoteLookup.FailedLocalIds.Contains(item.Id))
                .ToArray();
            var plan = AnnotateTodoReminderCleanup(
                BuildSyncPlan(executableDirty, remoteLookup.Events, settings.SyncConflictPolicy, RemoteSyncMode.Incremental),
                executableDirty);
            var execution = await ExecuteSyncPlanAsync(
                client,
                calendarId,
                plan,
                failures,
                reminderDefaults,
                settings.AdoptGoogleEmailRemindersAsLocalNotifications,
                cancellationToken);
            pushed += execution.Pushed;
            pulled += execution.Pulled;
            skipped += execution.Skipped;
            conflicts += execution.Conflicts;
            failed += remoteLookup.FailedLocalIds.Count + execution.Failed;
            deleted += execution.Deleted;
            recreated += execution.Recreated;
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
            $"選択対象の再同期: 送信 {pushed} / 失敗 {failed} / 削除 {deleted} / 再作成 {recreated}");
        await SaveFailureDiagnosticsAsync(failures, localIds);
        await SaveSyncResultAsync(result, settings.EnableSyncDiagnostics);
        return result;
    }

    public async Task<SyncResult> DiscardLocalChangesAsync(AppSettings settings, IReadOnlySet<string> localIds, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.Now;
        EnsureOAuthSettings(settings);

        var client = await _googleCalendarApi.CreateClientAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var reminderDefaults = await LoadCalendarReminderDefaultsAsync(client, cancellationToken);
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
                    localEvent.IsDeleted = true;
                    await _repository.UpsertSyncedEventAsync(localEvent);
                    restored++;
                    continue;
                }

                await _repository.UpsertSyncedEventAsync(GoogleEventMapper.FromGoogleEvent(
                    googleEvent,
                    localEvent.CalendarId,
                    GetDefaultReminders(reminderDefaults, localEvent.CalendarId),
                    settings.AdoptGoogleEmailRemindersAsLocalNotifications));
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
        await SaveFailureDiagnosticsAsync(failures, localIds);
        await SaveSyncResultAsync(result, settings.EnableSyncDiagnostics);
        return result;
    }

    public async Task<int> PullAsync(AppSettings settings, IEnumerable<string>? calendarIds = null, CancellationToken cancellationToken = default)
    {
        EnsureOAuthSettings(settings);

        var client = await _googleCalendarApi.CreateClientAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var reminderDefaults = await LoadCalendarReminderDefaultsAsync(client, cancellationToken);
        var targets = calendarIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (targets is null || targets.Length == 0)
        {
            targets = ResolveConfiguredTargetCalendarIds(settings).ToArray();
        }

        var pulled = 0;
        var allFailures = new List<SyncFailureDiagnostic>();
        foreach (var calendarId in targets)
        {
            var failures = new List<SyncFailureDiagnostic>();
            var pull = await PullRemoteEventsAsync(client, calendarId, settings.SyncConflictPolicy, failures, reminderDefaults, settings.AdoptGoogleEmailRemindersAsLocalNotifications, cancellationToken);
            pulled += pull.Pulled;
            allFailures.AddRange(failures);
        }

        await SavePullFailureDiagnosticsAsync(allFailures, targets);
        return pulled;
    }

    public async Task<int> RefreshReminderMetadataAsync(
        AppSettings settings,
        DateTimeOffset timeMin,
        DateTimeOffset timeMax,
        CancellationToken cancellationToken = default)
    {
        var result = await RefreshRemoteEventsAndReminderMetadataAsync(settings, timeMin, timeMax, cancellationToken);
        return result.TotalAffected;
    }

    public async Task<GoogleReminderRefreshResult> RefreshRemoteEventsAndReminderMetadataAsync(
        AppSettings settings,
        DateTimeOffset timeMin,
        DateTimeOffset timeMax,
        CancellationToken cancellationToken = default)
    {
        EnsureOAuthSettings(settings);

        var client = await _googleCalendarApi.CreateClientAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var reminderDefaults = await LoadCalendarReminderDefaultsAsync(client, cancellationToken);
        return await RefreshRemoteEventsAndReminderMetadataCoreAsync(
            client,
            reminderDefaults,
            settings,
            timeMin,
            timeMax,
            cancellationToken);
    }

    private async Task<GoogleReminderRefreshResult> RefreshRemoteEventsAndReminderMetadataCoreAsync(
        IGoogleCalendarClient client,
        IReadOnlyDictionary<string, IReadOnlyList<GoogleReminderOverride>> reminderDefaults,
        AppSettings settings,
        DateTimeOffset timeMin,
        DateTimeOffset timeMax,
        CancellationToken cancellationToken)
    {
        var updated = 0;
        var upserted = 0;
        var skipped = 0;

        foreach (var calendarId in await ResolveTargetCalendarIdsAsync(settings))
        {
            string? pageToken = null;
            do
            {
                var page = await client.ListEventsAsync(
                    new GoogleEventListRequest(
                        calendarId,
                        SyncToken: null,
                        pageToken,
                        timeMin,
                        ShowDeleted: false,
                        SingleEvents: false,
                        MaxResults: 2500,
                        TimeMax: timeMax),
                    cancellationToken);
                foreach (var googleEvent in page.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(googleEvent.Id)
                        || string.Equals(googleEvent.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        continue;
                    }

                    var mapped = GoogleEventMapper.FromGoogleEvent(
                        googleEvent,
                        calendarId,
                        GetDefaultReminders(reminderDefaults, calendarId),
                        settings.AdoptGoogleEmailRemindersAsLocalNotifications);
                    var local = await _repository.FindEventByGoogleEventIdAsync(calendarId, googleEvent.Id);
                    if (local is null)
                    {
                        await _repository.UpsertSyncedEventAsync(mapped);
                        upserted++;
                    }
                    else if (await _repository.UpdateGoogleReminderMetadataAsync(
                        calendarId,
                        googleEvent.Id,
                        mapped.ReminderMinutesBeforeStart,
                        mapped.EffectiveAppReminderMinutesBeforeStart,
                        mapped.EffectiveGoogleEmailReminderMinutesBeforeStart,
                        mapped.GoogleReminderMetadata))
                    {
                        updated++;
                    }
                    else
                    {
                        skipped++;
                    }
                }

                pageToken = page.NextPageToken;
            }
            while (!string.IsNullOrWhiteSpace(pageToken));
        }

        var result = new GoogleReminderRefreshResult(updated, upserted, skipped);
        Debug.WriteLine($"RefreshReminderMetadata updated={updated} upserted={upserted} skipped={skipped} range={timeMin:O}..{timeMax:O}");
        return result;
    }

    public async Task<SyncPreview> PreviewAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        EnsureOAuthSettings(settings);

        var client = await _googleCalendarApi.CreateClientAsync(settings.OAuthClientJsonPath!, cancellationToken);
        var reminderDefaults = await LoadCalendarReminderDefaultsAsync(client, cancellationToken);
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

            RemoteDelta remoteDelta;
            IReadOnlyList<SyncPlanItem> previewPlan;
            ResolvedSyncPlan resolved;
            var previewFailures = new List<SyncFailureDiagnostic>();
            try
            {
                resolved = await ResolveSyncPlanAsync(client, calendarId, calendarDirty, syncToken,
                    settings.SyncConflictPolicy, isPreview: true, previewFailures, cancellationToken);
                if (!resolved.Delta.Success)
                {
                    errorItems.Add(CreateCalendarPreviewFailure(calendarId, previewFailures));
                    continue;
                }
                remoteDelta = resolved.Delta with { Events = resolved.EffectiveRemoteEvents };
                previewPlan = resolved.Plan;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errorItems.Add(new SyncPreviewItem(calendarId, null, null, "Preview error", null, "error", ex.Message));
                continue;
            }

            foreach (var failedLocalId in resolved.FailedLocalIds)
            {
                var localEvent = calendarDirty.FirstOrDefault(item => item.Id == failedLocalId)
                    ?? await _repository.FindEventByIdAsync(failedLocalId);
                if (localEvent is null) continue;
                var failure = previewFailures.LastOrDefault(item =>
                    item.LocalId == failedLocalId
                    || !string.IsNullOrWhiteSpace(localEvent.GoogleEventId) && item.GoogleEventId == localEvent.GoogleEventId);
                errorItems.Add(CreateDirtyLookupPreviewFailure(calendarId, localEvent, failure));
            }

            foreach (var localEvent in previewPlan
                         .Where(item => item.Action == SyncPlanAction.PushLocal && item.LocalEvent is not null)
                         .Select(item => item.LocalEvent!)
                         .DistinctBy(item => item.Id))
            {
                var planItem = previewPlan.FirstOrDefault(item => string.Equals(item.LocalEvent?.Id, localEvent.Id, StringComparison.Ordinal));
                if (planItem?.Action != SyncPlanAction.PushLocal)
                {
                    continue;
                }

                var wasNotFound = resolved.NotFoundLocalIds.Contains(localEvent.Id);
                var detail = wasNotFound
                    ? localEvent.IsDeleted ? "Google側では既に削除済み" : "Google側に存在しないため再作成予定"
                    : planItem.RequiresTodoReminderCleanup
                        ? "Googleへ送信予定。同じ更新でGoogle側の通知も削除します。アプリ内通知は期限日の08:15です。"
                        : localEvent.IsDeleted ? "Googleから削除予定" : "Googleへ送信予定";
                var item = ToPreviewItem(calendarId, localEvent, localEvent.IsDeleted ? "delete" : "push", detail);
                if (planItem.RequiresTodoReminderCleanup)
                {
                    item = item with { ChangeFields = EventDirtyFieldTracker.MergeFieldNames(item.ChangeFields, "Reminder") };
                }
                var remoteForDiff = planItem.RemoteEvent is { } plannedRemoteEvent
                    ? GoogleEventMapper.FromGoogleEvent(
                        plannedRemoteEvent,
                        calendarId,
                        GetDefaultReminders(reminderDefaults, calendarId),
                        settings.AdoptGoogleEmailRemindersAsLocalNotifications)
                    : wasNotFound ? null : await TryLoadRemoteForPreviewAsync(
                        client,
                        calendarId,
                        localEvent,
                        reminderDefaults,
                        settings.AdoptGoogleEmailRemindersAsLocalNotifications,
                        cancellationToken);
                item = item with { FieldDiffs = remoteForDiff is null ? [] : BuildFieldDiffs(localEvent, remoteForDiff, "LocalToGoogle") };

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
                foreach (var googleEvent in remoteDelta.Events)
                {
                    var remoteEvent = GoogleEventMapper.FromGoogleEvent(
                        googleEvent,
                        calendarId,
                        GetDefaultReminders(reminderDefaults, calendarId),
                        settings.AdoptGoogleEmailRemindersAsLocalNotifications);
                    var item = ToPreviewItem(calendarId, remoteEvent, "pull", "Googleから取得予定");
                    var local = await _repository.FindEventByGoogleEventIdAsync(calendarId, googleEvent.Id);
                    var planItem = previewPlan.FirstOrDefault(entry => string.Equals(entry.RemoteEvent?.Id, googleEvent.Id, StringComparison.Ordinal));
                    if (planItem is { Action: SyncPlanAction.SkipConflict, LocalEvent: not null } plannedConflict)
                    {
                        local = plannedConflict.LocalEvent;
                        conflictItems.Add(item with { Detail = $"ローカル未同期変更とGoogle変更が競合: {settings.SyncConflictPolicy}" });
                        var conflictIndex = conflictItems.Count - 1;
                        conflictItems[conflictIndex] = conflictItems[conflictIndex] with
                        {
                            LocalId = local.Id,
                            ChangeFields = planItem.RequiresTodoReminderCleanup
                                ? EventDirtyFieldTracker.MergeFieldNames(local.DirtyFields, "Reminder")
                                : local.DirtyFields,
                            Detail = planItem.RequiresTodoReminderCleanup
                                ? "通常フィールドは競合のためスキップします。Google側の通知だけ削除予定です。ローカルの未同期変更は維持されます。"
                                : $"Conflict: {settings.SyncConflictPolicy}",
                            FieldDiffs = BuildFieldDiffs(local, remoteEvent, "Conflict")
                        };
                    }
                    else if (planItem?.Action == SyncPlanAction.PullRemote && remoteEvent.IsDeleted)
                    {
                        deleteItems.Add(item with { Kind = "remote-delete", Detail = "Google側削除を反映予定" });
                    }
                    else if (planItem?.Action == SyncPlanAction.PullRemote)
                    {
                        pullItems.Add(planItem.RequiresTodoReminderCleanup
                            ? item with
                            {
                                LocalId = planItem.LocalEvent?.Id,
                                ChangeFields = "Reminder",
                                Detail = planItem.LocalEvent?.IsDirty == true
                                    ? "Google側の通知を削除してから取得予定。アプリ内通知は期限日の08:15です。"
                                    : "Google側の通知を削除予定。アプリ内通知は期限日の08:15です。"
                            }
                            : item);
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
        var dirtyIds = dirtyEvents.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var activeFailures = failures
            .Where(failure => string.IsNullOrWhiteSpace(failure.LocalId) || dirtyIds.Contains(failure.LocalId))
            .ToArray();
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
            .Select(item => ToDirtyItem(item, activeFailures.LastOrDefault(failure => string.Equals(failure.LocalId, item.Id, StringComparison.Ordinal))))
            .ToArray();
        return new SyncDiagnosticsSnapshot(history.FirstOrDefault(), history, calendars, dirtyEvents.Count, dirtyItems, activeFailures);
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
                : PushNormalEventAsync(client, calendarId, localEvent, cancellationToken),
                _logger);
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
        Func<Task<SyncPushOutcome>> push,
        IAppLogger? logger)
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
            logger?.LogError(ex, $"Google Calendar push failed: {operation}");
            failures.Add(CreateFailureDiagnostic(localEvent, operation, ex));
            return SyncPushOutcome.Failed;
        }
    }

    private async Task<SyncPushOutcome> PushNormalEventAsync(
        IGoogleCalendarClient client,
        string calendarId,
        CalendarEvent localEvent,
        CancellationToken cancellationToken,
        Event? plannedRemoteEvent = null)
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

        var localPayload = GoogleEventMapper.ToGoogleEvent(localEvent);
        localEvent.GoogleReminderMetadata = GoogleEventMapper.FromGoogleEvent(localPayload, calendarId).GoogleReminderMetadata;
        Debug.WriteLine($"Push event Title={localEvent.Title} Description={localEvent.Description} Location={localEvent.Location} Start={localEvent.Start:O} End={localEvent.End:O} GoogleEventId={localEvent.GoogleEventId} CalendarId={calendarId} IsDirty={localEvent.IsDirty} DirtyFields={localEvent.DirtyFields}");
        if (string.IsNullOrWhiteSpace(localEvent.GoogleEventId))
        {
            var inserted = await client.InsertEventAsync(calendarId, localPayload, cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, inserted.Id, inserted.ETag);
            return SyncPushOutcome.Pushed;
        }

        try
        {
            var remoteEvent = plannedRemoteEvent ?? await client.GetEventAsync(calendarId, localEvent.GoogleEventId, cancellationToken);
            ApplyAppOwnedFields(remoteEvent, localPayload, includeOriginalStartTime: false, includeRecurrence: true);
            var updated = await UpdateEventAsync(client, calendarId, localEvent.GoogleEventId, remoteEvent, remoteEvent.ETag, cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, lastSyncedGoogleEtag: updated.ETag);
            Debug.WriteLine($"Push update succeeded and marked synced: {localEvent.Id}");
            return SyncPushOutcome.Pushed;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            var inserted = await client.InsertEventAsync(calendarId, localPayload, cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, inserted.Id, inserted.ETag);
            return SyncPushOutcome.RecreatedEvent;
        }
    }


    private static void ApplyAppOwnedFields(Event destination, Event source, bool includeOriginalStartTime, bool includeRecurrence)
    {
        destination.Summary = source.Summary;
        destination.Description = source.Description;
        destination.Location = source.Location;
        destination.ColorId = source.ColorId;
        destination.Start = source.Start;
        destination.End = source.End;
        destination.Status = source.Status;
        destination.Reminders = source.Reminders;
        if (includeRecurrence)
        {
            destination.Recurrence = source.Recurrence?.ToArray();
        }
        if (includeOriginalStartTime)
        {
            destination.OriginalStartTime = source.OriginalStartTime;
        }
    }

    private static Task<Event> UpdateEventAsync(
        IGoogleCalendarClient client,
        string calendarId,
        string eventId,
        Event googleEvent,
        string? etag,
        CancellationToken cancellationToken)
    {
        return client is IConditionalGoogleCalendarClient conditionalClient
            ? conditionalClient.UpdateEventAsync(calendarId, eventId, googleEvent, cancellationToken, etag)
            : client.UpdateEventAsync(calendarId, eventId, googleEvent, cancellationToken);
    }

    private async Task<SyncPushOutcome> PushRecurrenceExceptionAsync(
        IGoogleCalendarClient client,
        string calendarId,
        CalendarEvent localEvent,
        CancellationToken cancellationToken,
        Event? plannedRemoteEvent = null)
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
            var remoteEvent = plannedRemoteEvent ?? await client.GetEventAsync(calendarId, remoteEventId, cancellationToken);
            ApplyAppOwnedFields(remoteEvent, GoogleEventMapper.ToGoogleEvent(localEvent), includeOriginalStartTime: true, includeRecurrence: false);
            localEvent.GoogleReminderMetadata = GoogleEventMapper.FromGoogleEvent(remoteEvent, calendarId).GoogleReminderMetadata;
            var updated = await UpdateEventAsync(client, calendarId, remoteEventId, remoteEvent, remoteEvent.ETag, cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, remoteEventId, updated.ETag);
            return SyncPushOutcome.Pushed;
        }
        catch (GoogleApiException ex) when (IsNotFound(ex))
        {
            var googleEvent = GoogleEventMapper.ToGoogleEvent(localEvent);
            localEvent.GoogleReminderMetadata = GoogleEventMapper.FromGoogleEvent(googleEvent, calendarId).GoogleReminderMetadata;
            var inserted = await client.InsertEventAsync(calendarId, googleEvent, cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, inserted.Id, inserted.ETag);
            return SyncPushOutcome.RecreatedEvent;
        }
    }

    private static bool IsNotFound(Exception ex)
    {
        return ex is GoogleApiException { HttpStatusCode: System.Net.HttpStatusCode.NotFound }
            || ex is KeyNotFoundException;
    }

    private static async Task<Event?> FindExactRemoteMatchAsync(
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
                return match;
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

    private static IReadOnlyList<SyncPlanItem> BuildSyncPlan(
        IReadOnlyList<CalendarEvent> dirtyEvents,
        IReadOnlyList<Event> remoteEvents,
        SyncConflictPolicy conflictPolicy,
        RemoteSyncMode remoteSyncMode)
    {
        var dirtyByGoogleId = dirtyEvents
            .Where(item => !string.IsNullOrWhiteSpace(item.GoogleEventId))
            .ToDictionary(item => item.GoogleEventId!, StringComparer.Ordinal);
        var plannedLocalIds = new HashSet<string>(StringComparer.Ordinal);
        var plan = new List<SyncPlanItem>();

        foreach (var remoteEvent in remoteEvents)
        {
            if (string.IsNullOrWhiteSpace(remoteEvent.Id))
            {
                continue;
            }

            if (dirtyByGoogleId.TryGetValue(remoteEvent.Id, out var dirtyLocal))
            {
                plannedLocalIds.Add(dirtyLocal.Id);
                var remoteIsUnchanged = !string.IsNullOrWhiteSpace(dirtyLocal.LastSyncedGoogleEtag)
                    && string.Equals(dirtyLocal.LastSyncedGoogleEtag, remoteEvent.ETag, StringComparison.Ordinal);
                plan.Add(new SyncPlanItem(
                    remoteIsUnchanged
                        ? SyncPlanAction.PushLocal
                        : conflictPolicy switch
                    {
                        SyncConflictPolicy.SkipLocalDirty => SyncPlanAction.SkipConflict,
                        SyncConflictPolicy.PreferLocal => SyncPlanAction.PushLocal,
                        SyncConflictPolicy.PreferGoogle => SyncPlanAction.PullRemote,
                        _ => throw new ArgumentOutOfRangeException(nameof(conflictPolicy))
                    },
                    dirtyLocal,
                    remoteEvent,
                    IsConflict: !remoteIsUnchanged));
                continue;
            }

            plan.Add(new SyncPlanItem(SyncPlanAction.PullRemote, null, remoteEvent, IsConflict: false));
        }

        foreach (var dirtyLocal in dirtyEvents.Where(item => !plannedLocalIds.Contains(item.Id)))
        {
            plan.Add(new SyncPlanItem(SyncPlanAction.PushLocal, dirtyLocal, null, IsConflict: false));
        }

        return plan;
    }

    private async Task<SyncPlanExecution> ExecuteSyncPlanAsync(
        IGoogleCalendarClient client,
        string calendarId,
        IReadOnlyList<SyncPlanItem> plan,
        ICollection<SyncFailureDiagnostic> failures,
        IReadOnlyDictionary<string, IReadOnlyList<GoogleReminderOverride>> reminderDefaults,
        bool adoptEmailRemindersAsLocalNotifications,
        CancellationToken cancellationToken)
    {
        var pushed = 0;
        var pulled = 0;
        var skipped = 0;
        var conflicts = 0;
        var failed = 0;
        var deleted = 0;
        var recreated = 0;

        foreach (var item in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executableItem = item;
            if (item.RequiresTodoReminderCleanup && item.Action != SyncPlanAction.PushLocal)
            {
                try
                {
                    var currentRemote = item.RemoteEvent!;
                    currentRemote.Reminders = TodoReminderPolicy.CreateGoogleRemindersDisabled();
                    var cleanedRemote = client is IConditionalGoogleCalendarClient conditionalClient
                        ? await conditionalClient.UpdateEventAsync(
                            calendarId, currentRemote.Id, currentRemote, cancellationToken, currentRemote.ETag)
                        : await client.UpdateEventAsync(calendarId, currentRemote.Id, currentRemote, cancellationToken);
                    executableItem = item with { RemoteEvent = cleanedRemote };

                    if (item.Action == SyncPlanAction.SkipConflict && item.LocalEvent is { } skippedLocal)
                    {
                        await _repository.ApplyTodoReminderCleanupStateAsync(
                            skippedLocal.Id,
                            preserveDirtyState: true);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var diagnostic = item.LocalEvent is { } localTodo
                        ? CreateFailureDiagnostic(localTodo, "TodoReminderCleanup", ex, "ToDoのGoogle通知削除に失敗しました。")
                            with { FailureCategory = "TodoReminderCleanup" }
                        : CreatePullFailureDiagnostic(calendarId, null, null, ex, "TodoReminderCleanup", "ToDoのGoogle通知削除に失敗しました。");
                    failures.Add(diagnostic);
                    failed++;
                    continue;
                }
            }

            switch (executableItem.Action)
            {
                case SyncPlanAction.SkipConflict:
                    skipped++;
                    conflicts++;
                    break;

                case SyncPlanAction.PullRemote:
                    try
                    {
                        await _repository.UpsertSyncedEventAsync(GoogleEventMapper.FromGoogleEvent(
                            executableItem.RemoteEvent!,
                            calendarId,
                            GetDefaultReminders(reminderDefaults, calendarId),
                            adoptEmailRemindersAsLocalNotifications));
                        pulled++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Add(CreatePullFailureDiagnostic(calendarId, null, null, ex, "PlanPull", ex.Message));
                        failed++;
                    }
                    break;

                case SyncPlanAction.PushLocal:
                    var localEvent = executableItem.LocalEvent!;
                    if (localEvent.IsTodoLike)
                    {
                        var requiresLocalCleanup = TodoReminderPolicy.RequiresLocalCleanup(localEvent)
                            || executableItem.RequiresTodoReminderCleanup;
                        TodoReminderPolicy.NormalizeLocalFields(localEvent);
                        if (!localEvent.IsDeleted && requiresLocalCleanup)
                        {
                            localEvent.IsDirty = true;
                            localEvent.DirtyFields = EventDirtyFieldTracker.MergeFieldNames(localEvent.DirtyFields, "Reminder");
                            await _repository.SaveEventAsync(localEvent);
                        }
                    }
                    var operation = GetPushOperation(localEvent);
                    var outcome = await TryPushEventAsync(localEvent, operation, failures, () => localEvent.IsRecurrenceException
                        ? PushRecurrenceExceptionAsync(client, calendarId, localEvent, cancellationToken, executableItem.RemoteEvent)
                        : PushNormalEventAsync(client, calendarId, localEvent, cancellationToken, executableItem.RemoteEvent), _logger);
                    if (!outcome.Success)
                    {
                        failed++;
                        break;
                    }

                    pushed++;
                    deleted += outcome.Deleted ? 1 : 0;
                    recreated += outcome.Recreated ? 1 : 0;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return new SyncPlanExecution(pushed, pulled, skipped, conflicts, failed, deleted, recreated);
    }

    private async Task<RemoteDelta> LoadRemoteDeltaAsync(
        IGoogleCalendarClient client,
        string calendarId,
        string? syncToken,
        ICollection<SyncFailureDiagnostic> failures,
        CancellationToken cancellationToken,
        bool persistTokenReset = true)
    {
        var events = new List<Event>();
        string? pageToken = null;
        string? nextSyncToken = null;
        var fullSyncStart = string.IsNullOrWhiteSpace(syncToken) ? DateTimeOffset.Now.AddYears(-5) : (DateTimeOffset?)null;
        try
        {
            do
            {
                var page = await client.ListEventsAsync(new GoogleEventListRequest(
                    calendarId,
                    syncToken,
                    pageToken,
                    fullSyncStart,
                    ShowDeleted: true,
                    SingleEvents: false,
                    MaxResults: 2500), cancellationToken);
                events.AddRange(page.Items);
                pageToken = page.NextPageToken;
                if (string.IsNullOrWhiteSpace(pageToken))
                {
                    nextSyncToken = page.NextSyncToken;
                }
            }
            while (!string.IsNullOrWhiteSpace(pageToken));

            return new RemoteDelta(
                events,
                nextSyncToken,
                true,
                string.IsNullOrWhiteSpace(syncToken) ? RemoteSyncMode.InitialFull : RemoteSyncMode.Incremental,
                fullSyncStart);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Gone)
        {
            failures.Add(CreatePullFailureDiagnostic(calendarId, syncToken, pageToken, ex, "SyncTokenExpired", "410 Gone: sync token was reset before retry."));
            if (persistTokenReset)
            {
                await _repository.SaveSyncTokenAsync(calendarId, null);
            }
            var recovery = await LoadRemoteDeltaAsync(client, calendarId, null, failures, cancellationToken, persistTokenReset);
            return recovery with { Mode = RemoteSyncMode.RecoveryFull };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add(CreatePullFailureDiagnostic(calendarId, syncToken, pageToken, ex, "Pull", ex.Message));
            return new RemoteDelta([], null, false, RemoteSyncMode.Incremental, null);
        }
    }

    private async Task<ResolvedSyncPlan> ResolveSyncPlanAsync(
        IGoogleCalendarClient client, string calendarId, IReadOnlyList<CalendarEvent> dirtyEvents,
        string? syncToken, SyncConflictPolicy conflictPolicy, bool isPreview,
        ICollection<SyncFailureDiagnostic> failures, CancellationToken cancellationToken)
    {
        var actualDirtyEvents = dirtyEvents.Select(TodoReminderPolicy.CloneForSyncPlanning).ToArray();
        var todoCleanupCandidates = (await _repository.LoadTodoEventsAsync())
            .Where(item => item.CalendarId == calendarId && TodoReminderPolicy.RequiresLocalCleanup(item))
            .Select(TodoReminderPolicy.CloneForSyncPlanning)
            .ToArray();
        var remoteLookupCandidates = actualDirtyEvents
            .Concat(todoCleanupCandidates)
            .DistinctBy(item => item.Id)
            .ToArray();

        var delta = await LoadRemoteDeltaAsync(client, calendarId, syncToken, failures, cancellationToken, !isPreview);
        if (!delta.Success)
        {
            return new(delta, [], [], new HashSet<string>(), remoteLookupCandidates.Select(e => e.Id).ToHashSet(StringComparer.Ordinal));
        }
        var lookup = await LoadRemoteEventsMissingFromFullSyncListAsync(client, calendarId, remoteLookupCandidates, delta, failures, cancellationToken);
        var cleanupLookup = delta.Mode == RemoteSyncMode.Incremental
            ? await LoadRemoteEventsForTodoCleanupAsync(client, calendarId, todoCleanupCandidates, delta.Events, failures, cancellationToken)
            : new RemoteDirtyLookupResult([], new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        var effective = delta.Events.Concat(lookup.Events).ToArray();
        effective = effective.Concat(cleanupLookup.Events).DistinctBy(item => item.Id).ToArray();
        var failedIds = lookup.FailedLocalIds.Concat(cleanupLookup.FailedLocalIds).ToHashSet(StringComparer.Ordinal);
        var notFoundIds = lookup.NotFoundLocalIds.Concat(cleanupLookup.NotFoundLocalIds).ToHashSet(StringComparer.Ordinal);
        var executable = actualDirtyEvents.Where(e => !failedIds.Contains(e.Id)).ToArray();
        var plan = AnnotateTodoReminderCleanup(
            BuildSyncPlan(executable, effective, conflictPolicy, delta.Mode),
            todoCleanupCandidates);
        return new(delta, effective, plan, notFoundIds, failedIds);
    }

    private static Task<RemoteDirtyLookupResult> LoadRemoteEventsForTodoCleanupAsync(
        IGoogleCalendarClient client,
        string calendarId,
        IReadOnlyList<CalendarEvent> candidates,
        IReadOnlyList<Event> listedEvents,
        ICollection<SyncFailureDiagnostic> failures,
        CancellationToken cancellationToken)
    {
        var listedIds = listedEvents.Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => item.Id!).ToHashSet(StringComparer.Ordinal);
        var missing = candidates.Where(item => !string.IsNullOrWhiteSpace(item.GoogleEventId)
            && !listedIds.Contains(item.GoogleEventId!)).ToArray();
        return LoadRemoteEventsForDirtyAsync(client, calendarId, missing, failures, cancellationToken);
    }

    private static IReadOnlyList<SyncPlanItem> AnnotateTodoReminderCleanup(
        IReadOnlyList<SyncPlanItem> plan,
        IReadOnlyList<CalendarEvent> cleanupCandidates)
    {
        var candidates = cleanupCandidates
            .Where(item => !string.IsNullOrWhiteSpace(item.GoogleEventId))
            .ToDictionary(item => item.GoogleEventId!, StringComparer.Ordinal);
        return plan.Select(item =>
        {
            if (item.RemoteEvent is not { } remote
                || string.Equals(remote.Status, "cancelled", StringComparison.OrdinalIgnoreCase)
                || !TodoReminderPolicy.HasGoogleReminders(remote)
                || !GoogleEventMapper.FromGoogleEvent(remote, item.LocalEvent?.CalendarId ?? "").IsTodoLike)
            {
                return item;
            }

            candidates.TryGetValue(remote.Id ?? string.Empty, out var candidate);
            return item with
            {
                LocalEvent = item.LocalEvent ?? candidate,
                RequiresTodoReminderCleanup = true
            };
        }).ToArray();
    }

    private static async Task<RemoteDirtyLookupResult> LoadRemoteEventsMissingFromFullSyncListAsync(
        IGoogleCalendarClient client,
        string calendarId,
        IReadOnlyList<CalendarEvent> dirtyEvents,
        RemoteDelta delta,
        ICollection<SyncFailureDiagnostic> failures,
        CancellationToken cancellationToken)
    {
        if (delta.Mode is not (RemoteSyncMode.InitialFull or RemoteSyncMode.RecoveryFull)
            )
        {
            return new RemoteDirtyLookupResult([], new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        }

        var listedGoogleEventIds = delta.Events
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => item.Id!)
            .ToHashSet(StringComparer.Ordinal);
        var missingFromList = dirtyEvents.Where(item =>
            !string.IsNullOrWhiteSpace(item.GoogleEventId)
            && !listedGoogleEventIds.Contains(item.GoogleEventId!));
        return await LoadRemoteEventsForDirtyAsync(client, calendarId, missingFromList.ToArray(), failures, cancellationToken);
    }

    private static async Task<RemoteDirtyLookupResult> LoadRemoteEventsForDirtyAsync(
        IGoogleCalendarClient client,
        string calendarId,
        IReadOnlyList<CalendarEvent> dirtyEvents,
        ICollection<SyncFailureDiagnostic> failures,
        CancellationToken cancellationToken)
    {
        var remoteEvents = new List<Event>();
        var notFoundLocalIds = new HashSet<string>(StringComparer.Ordinal);
        var failedLocalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var localEvent in dirtyEvents.Where(item => !string.IsNullOrWhiteSpace(item.GoogleEventId)))
        {
            try
            {
                remoteEvents.Add(await client.GetEventAsync(calendarId, localEvent.GoogleEventId!, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && IsNotFound(ex))
            {
                notFoundLocalIds.Add(localEvent.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !IsNotFound(ex))
            {
                failures.Add(CreateFailureDiagnostic(localEvent, "LoadRemoteForDirty", ex));
                failedLocalIds.Add(localEvent.Id);
            }
        }

        return new RemoteDirtyLookupResult(remoteEvents, notFoundLocalIds, failedLocalIds);
    }

    private async Task<SyncPullSummary> PullRemoteEventsAsync(
        IGoogleCalendarClient client,
        string calendarId,
        SyncConflictPolicy conflictPolicy,
        ICollection<SyncFailureDiagnostic> failures,
        IReadOnlyDictionary<string, IReadOnlyList<GoogleReminderOverride>> reminderDefaults,
        bool adoptEmailRemindersAsLocalNotifications,
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

                    await _repository.UpsertSyncedEventAsync(GoogleEventMapper.FromGoogleEvent(
                        googleEvent,
                        calendarId,
                        GetDefaultReminders(reminderDefaults, calendarId),
                        adoptEmailRemindersAsLocalNotifications));
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
            var retry = await PullRemoteEventsAsync(client, calendarId, conflictPolicy, failures, reminderDefaults, adoptEmailRemindersAsLocalNotifications, cancellationToken);
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

    private async Task<CalendarEvent?> TryLoadRemoteForPreviewAsync(
        IGoogleCalendarClient client,
        string calendarId,
        CalendarEvent localEvent,
        IReadOnlyDictionary<string, IReadOnlyList<GoogleReminderOverride>> reminderDefaults,
        bool adoptEmailRemindersAsLocalNotifications,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localEvent.GoogleEventId))
        {
            return null;
        }

        try
        {
            var googleEvent = await client.GetEventAsync(calendarId, localEvent.GoogleEventId, cancellationToken);
            return GoogleEventMapper.FromGoogleEvent(
                googleEvent,
                calendarId,
                GetDefaultReminders(reminderDefaults, calendarId),
                adoptEmailRemindersAsLocalNotifications);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    private static IReadOnlyList<SyncFieldDiff> BuildFieldDiffs(CalendarEvent localEvent, CalendarEvent googleEvent, string direction)
    {
        return
        [
            Diff("Title", "件名", localEvent.Title, googleEvent.Title, direction),
            Diff("Start", "開始", FormatDateTime(localEvent.Start, localEvent.IsAllDay), FormatDateTime(googleEvent.Start, googleEvent.IsAllDay), direction),
            Diff("End", "終了", FormatDateTime(localEvent.End, localEvent.IsAllDay), FormatDateTime(googleEvent.End, googleEvent.IsAllDay), direction),
            Diff("AllDay", "終日", localEvent.IsAllDay ? "ON" : "OFF", googleEvent.IsAllDay ? "ON" : "OFF", direction),
            Diff("Description", "内容", localEvent.Description ?? "", googleEvent.Description ?? "", direction),
            Diff("Location", "場所", localEvent.Location ?? "", googleEvent.Location ?? "", direction),
            Diff("Calendar", "カレンダー", localEvent.CalendarId, googleEvent.CalendarId, direction),
            Diff("Color", "色", localEvent.ColorId ?? "", googleEvent.ColorId ?? "", direction),
            Diff("Reminder", "通知", FormatReminderDiffValue(localEvent), FormatReminderDiffValue(googleEvent), direction),
            Diff("Deleted", "削除", localEvent.IsDeleted ? "削除" : "通常", googleEvent.IsDeleted ? "削除" : "通常", direction)
        ];
    }

    private static SyncFieldDiff Diff(string fieldName, string displayName, string localValue, string googleValue, string direction)
    {
        return new SyncFieldDiff(
            fieldName,
            displayName,
            localValue,
            googleValue,
            direction,
            !string.Equals(localValue, googleValue, StringComparison.Ordinal));
    }

    private static string FormatDateTime(DateTimeOffset value, bool isAllDay)
    {
        return isAllDay ? value.ToString("yyyy/MM/dd") : value.ToString("yyyy/MM/dd HH:mm");
    }

    private static string FormatReminderDiffValue(CalendarEvent calendarEvent)
    {
        var parts = new List<string>();
        foreach (var minutes in calendarEvent.EffectiveAppReminderMinutesBeforeStart)
        {
            parts.Add($"popup {minutes}");
        }

        foreach (var minutes in calendarEvent.EffectiveGoogleEmailReminderMinutesBeforeStart)
        {
            parts.Add($"email {minutes}");
        }

        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    private static SyncPreviewItem ToPreviewItem(string calendarId, CalendarEvent calendarEvent, string kind, string detail, IReadOnlyList<SyncFieldDiff>? fieldDiffs = null)
    {
        return new SyncPreviewItem(
            calendarId,
            calendarEvent.Id,
            calendarEvent.GoogleEventId,
            string.IsNullOrWhiteSpace(calendarEvent.Title) ? "(no title)" : calendarEvent.Title,
            calendarEvent.Start,
            kind,
            detail,
            calendarEvent.DirtyFields,
            fieldDiffs ?? []);
    }

    private static SyncPreviewItem CreateCalendarPreviewFailure(
        string calendarId, IReadOnlyList<SyncFailureDiagnostic> failures)
    {
        var failure = failures.LastOrDefault();
        var metadata = failure is null ? "分類: RemoteList / 再試行可能: はい" :
            $"分類: {failure.FailureCategory ?? failure.Operation} / HTTP: {failure.HttpStatusCode ?? "不明"} / 再試行可能: はい";
        return new SyncPreviewItem(calendarId, null, null, "Google予定を取得できませんでした", null, "error",
            $"このカレンダーの同期プレビューを作成できませんでした。ローカル予定は変更されていません。{metadata}");
    }

    private static SyncPreviewItem CreateDirtyLookupPreviewFailure(
        string calendarId, CalendarEvent localEvent, SyncFailureDiagnostic? failure)
    {
        var metadata = $"分類: {failure?.FailureCategory ?? failure?.Operation ?? "RemoteDirtyLookup"}"
            + $" / HTTP: {failure?.HttpStatusCode ?? "不明"} / 再試行が必要です。";
        return ToPreviewItem(calendarId, localEvent, "error",
            $"Google側予定の確認に失敗したため、この予定は今回の同期対象から除外されます。{metadata}");
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

    private async Task SaveFailureDiagnosticsAsync(
        IReadOnlyCollection<SyncFailureDiagnostic> failures,
        IReadOnlySet<string> attemptedLocalIds)
    {
        var attemptedIds = attemptedLocalIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        if (attemptedIds.Count == 0)
        {
            await SaveFailureDiagnosticsAsync(failures);
            return;
        }

        var dirtyIds = (await _repository.LoadDirtyEventsAsync())
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var existing = await LoadFailureDiagnosticsAsync();
        var merged = existing
            .Where(failure => string.IsNullOrWhiteSpace(failure.LocalId)
                || !attemptedIds.Contains(failure.LocalId) && dirtyIds.Contains(failure.LocalId))
            .Concat(failures)
            .ToArray();

        await SaveFailureDiagnosticsAsync(merged);
    }

    private async Task SavePullFailureDiagnosticsAsync(
        IReadOnlyCollection<SyncFailureDiagnostic> failures,
        IReadOnlyCollection<string> attemptedCalendarIds)
    {
        var attemptedCalendars = attemptedCalendarIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        if (attemptedCalendars.Count == 0)
        {
            await SaveFailureDiagnosticsAsync(failures);
            return;
        }

        var dirtyIds = (await _repository.LoadDirtyEventsAsync())
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var existing = await LoadFailureDiagnosticsAsync();
        var merged = existing
            .Where(failure => !string.IsNullOrWhiteSpace(failure.LocalId)
                ? dirtyIds.Contains(failure.LocalId)
                : !attemptedCalendars.Contains(failure.CalendarId))
            .Concat(failures)
            .ToArray();

        await SaveFailureDiagnosticsAsync(merged);
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
internal enum SyncPlanAction
{
    PushLocal,
    PullRemote,
    SkipConflict
}

internal enum RemoteSyncMode
{
    InitialFull,
    Incremental,
    RecoveryFull
}

internal sealed record SyncPlanItem(
    SyncPlanAction Action,
    CalendarEvent? LocalEvent,
    Event? RemoteEvent,
    bool IsConflict,
    bool RequiresTodoReminderCleanup = false);

internal sealed record SyncPlanExecution(
    int Pushed,
    int Pulled,
    int Skipped,
    int Conflicts,
    int Failed,
    int Deleted,
    int Recreated);

internal sealed record RemoteDirtyLookupResult(IReadOnlyList<Event> Events, IReadOnlySet<string> NotFoundLocalIds, IReadOnlySet<string> FailedLocalIds);

internal sealed record ResolvedSyncPlan(RemoteDelta Delta, IReadOnlyList<Event> EffectiveRemoteEvents,
    IReadOnlyList<SyncPlanItem> Plan, IReadOnlySet<string> NotFoundLocalIds, IReadOnlySet<string> FailedLocalIds);

internal sealed record RemoteDelta(
    IReadOnlyList<Event> Events,
    string? NextSyncToken,
    bool Success,
    RemoteSyncMode Mode,
    DateTimeOffset? FullSyncStart);
public sealed record GoogleReminderRefreshResult(int UpdatedExisting, int UpsertedMissing, int Skipped)
{
    public int TotalAffected => UpdatedExisting + UpsertedMissing;
}

internal sealed record SyncPushOutcome(bool Success, bool Deleted, bool Recreated)
{
    public static SyncPushOutcome Pushed { get; } = new(true, false, false);
    public static SyncPushOutcome Failed { get; } = new(false, false, false);
    public static SyncPushOutcome DeletedEvent { get; } = new(true, true, false);
    public static SyncPushOutcome RecreatedEvent { get; } = new(true, false, true);
}
