using FavGCalSchedulerClone.App.Models;
using Google.Apis.Calendar.v3.Data;

namespace FavGCalSchedulerClone.App.Services;

public interface IGoogleCalendarApi
{
    Task<IGoogleCalendarClient> CreateClientAsync(string clientJsonPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, EventDisplayColors>> LoadEventColorPaletteAsync(CancellationToken cancellationToken = default);
    Task ClearTokensAsync();
}

public interface IGoogleCalendarClient
{
    Task<IReadOnlyList<GoogleCalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default);
    Task<Event> InsertEventAsync(string calendarId, Event googleEvent, CancellationToken cancellationToken = default);
    Task<Event> UpdateEventAsync(string calendarId, string eventId, Event googleEvent, CancellationToken cancellationToken = default);
    Task DeleteEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default);
    Task<Event> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default);
    Task<GoogleEventPage> ListEventsAsync(GoogleEventListRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Event>> ListInstancesAsync(
        string calendarId,
        string recurringEventId,
        DateTimeOffset timeMin,
        DateTimeOffset timeMax,
        bool showDeleted,
        int maxResults,
        CancellationToken cancellationToken = default);
}

public sealed record GoogleEventListRequest(
    string CalendarId,
    string? SyncToken,
    string? PageToken,
    DateTimeOffset? TimeMin,
    bool ShowDeleted,
    bool SingleEvents,
    int MaxResults,
    DateTimeOffset? TimeMax = null);

public sealed record GoogleEventPage(
    IReadOnlyList<Event> Items,
    string? NextPageToken,
    string? NextSyncToken);
