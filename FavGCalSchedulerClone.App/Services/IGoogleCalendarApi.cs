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

internal interface IConditionalGoogleCalendarClient : IGoogleCalendarClient
{
    Task<Event> UpdateEventAsync(
        string calendarId,
        string eventId,
        Event googleEvent,
        CancellationToken cancellationToken,
        string? ifMatchETag);
}

public sealed record GoogleEventListRequest
{
    public GoogleEventListRequest(
        string CalendarId,
        string? SyncToken,
        string? PageToken,
        DateTimeOffset? TimeMin,
        bool ShowDeleted,
        bool SingleEvents,
        int MaxResults,
        DateTimeOffset? TimeMax = null)
    {
        this.CalendarId = CalendarId;
        this.SyncToken = SyncToken;
        this.PageToken = PageToken;
        this.ShowDeleted = ShowDeleted;
        this.SingleEvents = SingleEvents;
        this.MaxResults = MaxResults;
        this.TimeMax = TimeMax;

        // Parent-event full synchronization must not use a lower TimeMin bound.
        // A series can start many years ago and still have current/future instances;
        // filtering the parent list would make that entire series disappear locally.
        this.TimeMin = ShowDeleted && !SingleEvents && TimeMax is null
            ? null
            : TimeMin;
    }

    public string CalendarId { get; init; }
    public string? SyncToken { get; init; }
    public string? PageToken { get; init; }
    public DateTimeOffset? TimeMin { get; init; }
    public bool ShowDeleted { get; init; }
    public bool SingleEvents { get; init; }
    public int MaxResults { get; init; }
    public DateTimeOffset? TimeMax { get; init; }
}

public sealed record GoogleEventPage(
    IReadOnlyList<Event> Items,
    string? NextPageToken,
    string? NextSyncToken);
