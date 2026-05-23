using System.Diagnostics;
using FavGCalSchedulerClone.App.Models;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using System.Text.Json;

namespace FavGCalSchedulerClone.App.Services;

public sealed class GoogleCalendarSyncService
{
    private const string EventColorPaletteSettingKey = "google-event-color-palette";
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
        if (string.IsNullOrWhiteSpace(settings.OAuthClientJsonPath) || !File.Exists(settings.OAuthClientJsonPath))
        {
            throw new InvalidOperationException("OAuth client JSONを設定してください。");
        }

        var service = await CreateServiceAsync(settings.OAuthClientJsonPath, cancellationToken);
        var pushed = 0;
        var pulled = 0;
        foreach (var calendarId in ResolveTargetCalendarIds(settings))
        {
            pushed += await PushDirtyEventsAsync(service, calendarId, cancellationToken);
            pulled += await PullRemoteEventsAsync(service, calendarId, cancellationToken);
        }

        return new SyncResult(pushed, pulled);
    }

    public async Task<int> PullAsync(AppSettings settings, IEnumerable<string>? calendarIds = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.OAuthClientJsonPath) || !File.Exists(settings.OAuthClientJsonPath))
        {
            throw new InvalidOperationException("OAuth client JSONを設定してください。");
        }

        var service = await CreateServiceAsync(settings.OAuthClientJsonPath, cancellationToken);
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
            pulled += await PullRemoteEventsAsync(service, calendarId, cancellationToken);
        }

        return pulled;
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

    private async Task<int> PushDirtyEventsAsync(CalendarService service, string calendarId, CancellationToken cancellationToken)
    {
        var dirtyEvents = (await _repository.LoadDirtyEventsAsync())
            .Where(e => e.CalendarId == calendarId)
            .ToArray();

        var recurringMasters = dirtyEvents.Where(item => item.IsRecurringMaster).OrderBy(item => item.UpdatedAt).ToArray();
        var standaloneEvents = dirtyEvents.Where(item => !item.IsRecurringMaster && !item.IsRecurrenceException).OrderBy(item => item.UpdatedAt).ToArray();
        var recurrenceExceptions = dirtyEvents.Where(item => item.IsRecurrenceException).OrderBy(item => item.UpdatedAt).ToArray();
        var pushed = 0;

        foreach (var localEvent in recurringMasters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryPushEventAsync(() => PushNormalEventAsync(service, calendarId, localEvent, cancellationToken)))
            {
                pushed++;
            }
        }

        foreach (var localEvent in standaloneEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryPushEventAsync(() => PushNormalEventAsync(service, calendarId, localEvent, cancellationToken)))
            {
                pushed++;
            }
        }

        foreach (var localEvent in recurrenceExceptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryPushEventAsync(() => PushRecurrenceExceptionAsync(service, calendarId, localEvent, cancellationToken)))
            {
                pushed++;
            }
        }

        return pushed;
    }

    private static async Task<bool> TryPushEventAsync(Func<Task<bool>> push)
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
            return false;
        }
    }

    private async Task<bool> PushNormalEventAsync(CalendarService service, string calendarId, CalendarEvent localEvent, CancellationToken cancellationToken)
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
                    return true;
                }
            }

            await _repository.MarkSyncedAsync(localEvent);
            return true;
        }

        var googleEvent = GoogleEventMapper.ToGoogleEvent(localEvent);
        if (string.IsNullOrWhiteSpace(localEvent.GoogleEventId))
        {
            var inserted = await service.Events.Insert(googleEvent, calendarId).ExecuteAsync(cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, inserted.Id);
        }
        else
        {
            try
            {
                await service.Events.Update(googleEvent, calendarId, localEvent.GoogleEventId).ExecuteAsync(cancellationToken);
                await _repository.MarkSyncedAsync(localEvent);
            }
            catch (GoogleApiException ex) when (IsNotFound(ex))
            {
                var inserted = await service.Events.Insert(googleEvent, calendarId).ExecuteAsync(cancellationToken);
                await _repository.MarkSyncedAsync(localEvent, inserted.Id);
            }
        }

        return true;
    }

    private async Task<bool> PushRecurrenceExceptionAsync(CalendarService service, string calendarId, CalendarEvent localEvent, CancellationToken cancellationToken)
    {
        var recurringEventId = await ResolveRecurringEventIdAsync(localEvent);
        if (string.IsNullOrWhiteSpace(recurringEventId))
        {
            return false;
        }

        var remoteEventId = localEvent.GoogleEventId;
        if (string.IsNullOrWhiteSpace(remoteEventId))
        {
            remoteEventId = await ResolveRemoteOccurrenceIdAsync(service, calendarId, recurringEventId, localEvent, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(remoteEventId))
        {
            return false;
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
                return true;
            }

            await _repository.MarkSyncedAsync(localEvent, remoteEventId);
            return true;
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
        }
        catch (GoogleApiException ex) when (IsNotFound(ex))
        {
            var inserted = await service.Events.Insert(GoogleEventMapper.ToGoogleEvent(localEvent), calendarId).ExecuteAsync(cancellationToken);
            await _repository.MarkSyncedAsync(localEvent, inserted.Id);
        }

        return true;
    }

    public static GoogleNotFoundSyncAction ResolveNotFoundAction(CalendarEvent localEvent)
    {
        return localEvent.IsDeleted
            ? GoogleNotFoundSyncAction.MarkLocalSynced
            : GoogleNotFoundSyncAction.RecreateRemote;
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

    private async Task<int> PullRemoteEventsAsync(CalendarService service, string calendarId, CancellationToken cancellationToken)
    {
        var syncToken = await _repository.GetSyncTokenAsync(calendarId);
        var pulled = 0;

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
            return await PullRemoteEventsAsync(service, calendarId, cancellationToken);
        }

        return pulled;
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

public sealed record SyncResult(int Pushed, int Pulled);
public enum GoogleNotFoundSyncAction
{
    MarkLocalSynced,
    RecreateRemote
}
