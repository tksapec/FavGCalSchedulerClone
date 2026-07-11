using FavGCalSchedulerClone.App.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace FavGCalSchedulerClone.App.Services;

public sealed class GoogleCalendarApi : IGoogleCalendarApi
{
    public Task ClearTokensAsync()
    {
        var store = new ProtectedFileDataStore(AppPaths.TokenDirectory);
        return store.ClearAsync();
    }

    public async Task<IReadOnlyDictionary<string, EventDisplayColors>> LoadEventColorPaletteAsync(CancellationToken cancellationToken = default)
    {
        using var service = new CalendarService(new BaseClientService.Initializer
        {
            ApplicationName = "FavGCalSchedulerClone"
        });
        var colors = await service.Colors.Get().ExecuteAsync(cancellationToken);
        return (colors.Event__ ?? new Dictionary<string, ColorDefinition>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Value.Background)
                           && !string.IsNullOrWhiteSpace(item.Value.Foreground))
            .ToDictionary(
                item => item.Key,
                item => new EventDisplayColors(item.Value.Background!, item.Value.Foreground!),
                StringComparer.Ordinal);
    }

    public async Task<IGoogleCalendarClient> CreateClientAsync(
        string clientJsonPath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(clientJsonPath);
        var secrets = GoogleClientSecrets.FromStream(stream);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            GoogleCalendarDefaults.CalendarScopes,
            "personal-user",
            cancellationToken,
            new ProtectedFileDataStore(AppPaths.TokenDirectory));

        var service = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "FavGCalSchedulerClone"
        });
        return new GoogleCalendarClient(service);
    }

    internal sealed class GoogleCalendarClient : IConditionalGoogleCalendarClient
    {
        private readonly CalendarService _service;

        public GoogleCalendarClient(CalendarService service)
        {
            _service = service;
        }

        public async Task<IReadOnlyList<GoogleCalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default)
        {
            var calendars = new Dictionary<string, GoogleCalendarInfo>(StringComparer.Ordinal);
            string? pageToken = null;
            do
            {
                var request = _service.CalendarList.List();
                request.PageToken = pageToken;
                var page = await request.ExecuteAsync(cancellationToken);
                foreach (var item in page.Items ?? [])
                {
                    if (string.IsNullOrWhiteSpace(item.Id))
                    {
                        continue;
                    }

                    calendars.TryAdd(item.Id, new GoogleCalendarInfo(
                        item.Id,
                        item.SummaryOverride ?? item.Summary ?? item.Id,
                        (item.DefaultReminders ?? [])
                            .Where(reminder => !string.IsNullOrWhiteSpace(reminder.Method) && reminder.Minutes is not null)
                            .Select(reminder => new GoogleReminderOverride(reminder.Method!, reminder.Minutes!.Value))
                            .ToArray()));
                }

                pageToken = page.NextPageToken;
            }
            while (!string.IsNullOrWhiteSpace(pageToken));

            return calendars.Values
                .OrderBy(item => item.Summary, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        public async Task<Event> InsertEventAsync(string calendarId, Event googleEvent, CancellationToken cancellationToken = default)
        {
            return await _service.Events.Insert(googleEvent, calendarId).ExecuteAsync(cancellationToken);
        }

        public Task<Event> UpdateEventAsync(string calendarId, string eventId, Event googleEvent, CancellationToken cancellationToken = default)
        {
            return UpdateEventAsync(calendarId, eventId, googleEvent, cancellationToken, null);
        }

        public async Task<Event> UpdateEventAsync(
            string calendarId,
            string eventId,
            Event googleEvent,
            CancellationToken cancellationToken = default,
            string? ifMatchETag = null)
        {
            var request = _service.Events.Update(googleEvent, calendarId, eventId);
            if (!string.IsNullOrWhiteSpace(ifMatchETag))
            {
                request.ModifyRequest = message => message.Headers.TryAddWithoutValidation("If-Match", ifMatchETag);
            }

            return await request.ExecuteAsync(cancellationToken);
        }

        public async Task DeleteEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default)
        {
            await _service.Events.Delete(calendarId, eventId).ExecuteAsync(cancellationToken);
        }

        public async Task<Event> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default)
        {
            return await _service.Events.Get(calendarId, eventId).ExecuteAsync(cancellationToken);
        }

        public async Task<GoogleEventPage> ListEventsAsync(
            GoogleEventListRequest request,
            CancellationToken cancellationToken = default)
        {
            var list = _service.Events.List(request.CalendarId);
            list.ShowDeleted = request.ShowDeleted;
            list.SingleEvents = request.SingleEvents;
            list.MaxResults = request.MaxResults;
            list.PageToken = request.PageToken;
            if (string.IsNullOrWhiteSpace(request.SyncToken))
            {
                list.TimeMinDateTimeOffset = request.TimeMin;
                list.TimeMaxDateTimeOffset = request.TimeMax;
            }
            else
            {
                list.SyncToken = request.SyncToken;
            }

            var page = await list.ExecuteAsync(cancellationToken);
            return new GoogleEventPage((page.Items ?? []).ToArray(), page.NextPageToken, page.NextSyncToken);
        }

        public async Task<IReadOnlyList<Event>> ListInstancesAsync(
            string calendarId,
            string recurringEventId,
            DateTimeOffset timeMin,
            DateTimeOffset timeMax,
            bool showDeleted,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            var request = _service.Events.Instances(calendarId, recurringEventId);
            request.TimeMinDateTimeOffset = timeMin;
            request.TimeMaxDateTimeOffset = timeMax;
            request.ShowDeleted = showDeleted;
            request.MaxResults = maxResults;
            var page = await request.ExecuteAsync(cancellationToken);
            return (page.Items ?? []).ToArray();
        }
    }
}
