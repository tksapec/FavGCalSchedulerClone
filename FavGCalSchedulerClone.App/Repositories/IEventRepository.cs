using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Repositories;

public interface IEventRepository
{
    Task InitializeAsync();
    Task<IReadOnlyList<CalendarEvent>> LoadEventsAsync(DateTimeOffset start, DateTimeOffset end, bool includeDeleted = false);
    Task<IReadOnlyList<CalendarEvent>> LoadTodoEventsAsync();
    Task<IReadOnlyList<CalendarEvent>> LoadDirtyEventsAsync();
    Task<CalendarEvent?> FindEventByGoogleEventIdAsync(string calendarId, string? googleEventId);
    Task<CalendarEvent?> FindDuplicateEventAsync(CalendarEvent calendarEvent);
    Task<CalendarEvent?> FindMasterByIdAsync(string? id);
    Task<IReadOnlyList<CalendarEvent>> LoadSeriesEventsAsync(string? recurringParentId, string? recurringEventId);
    Task SaveEventAsync(CalendarEvent calendarEvent);
    Task UpsertSyncedEventAsync(CalendarEvent calendarEvent);
    Task MarkSyncedAsync(CalendarEvent calendarEvent, string? googleEventId = null);
    Task DeleteEventAsync(CalendarEvent calendarEvent);
}
