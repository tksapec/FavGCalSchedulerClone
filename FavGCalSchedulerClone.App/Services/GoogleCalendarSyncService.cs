using FavGCalSchedulerClone.App.Models;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;

namespace FavGCalSchedulerClone.App.Services;

public sealed class GoogleCalendarSyncService
{
    private readonly CalendarRepository _repository;

    public GoogleCalendarSyncService(CalendarRepository repository)
    {
        _repository = repository;
    }

    public async Task AuthorizeAsync(string clientJsonPath, CancellationToken cancellationToken = default)
    {
        _ = await CreateServiceAsync(clientJsonPath, cancellationToken);
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
        var calendarId = settings.ActiveCalendarId;
        var pushed = await PushDirtyEventsAsync(service, calendarId, cancellationToken);
        var pulled = await PullRemoteEventsAsync(service, calendarId, cancellationToken);
        return new SyncResult(pushed, pulled);
    }

    private async Task<int> PushDirtyEventsAsync(CalendarService service, string calendarId, CancellationToken cancellationToken)
    {
        var dirtyEvents = await _repository.LoadDirtyEventsAsync();
        var pushed = 0;

        foreach (var localEvent in dirtyEvents.Where(e => e.CalendarId == calendarId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (localEvent.IsDeleted)
            {
                if (!string.IsNullOrWhiteSpace(localEvent.GoogleEventId))
                {
                    await service.Events.Delete(calendarId, localEvent.GoogleEventId).ExecuteAsync(cancellationToken);
                }

                await _repository.MarkSyncedAsync(localEvent);
                pushed++;
                continue;
            }

            var googleEvent = GoogleEventMapper.ToGoogleEvent(localEvent);
            if (string.IsNullOrWhiteSpace(localEvent.GoogleEventId))
            {
                var inserted = await service.Events.Insert(googleEvent, calendarId).ExecuteAsync(cancellationToken);
                await _repository.MarkSyncedAsync(localEvent, inserted.Id);
            }
            else
            {
                await service.Events.Update(googleEvent, calendarId, localEvent.GoogleEventId).ExecuteAsync(cancellationToken);
                await _repository.MarkSyncedAsync(localEvent);
            }

            pushed++;
        }

        return pushed;
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
                request.SingleEvents = true;
                request.MaxResults = 2500;
                request.PageToken = pageToken;
                if (string.IsNullOrWhiteSpace(syncToken))
                {
                    request.TimeMinDateTimeOffset = DateTimeOffset.Now.AddYears(-1);
                    request.TimeMaxDateTimeOffset = DateTimeOffset.Now.AddYears(2);
                    request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
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
