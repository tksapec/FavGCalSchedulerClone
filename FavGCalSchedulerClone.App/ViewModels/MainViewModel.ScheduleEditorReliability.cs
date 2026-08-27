using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    internal string ResolveScheduleEditorCalendarId(CalendarEvent? editingEvent)
    {
        var eventCalendarId = editingEvent?.CalendarId;
        if (!string.IsNullOrWhiteSpace(eventCalendarId)
            && AvailableCalendars.Any(item => string.Equals(item.Id, eventCalendarId, StringComparison.Ordinal)))
        {
            return eventCalendarId;
        }

        // Preserve a non-empty historical/source calendar that is temporarily
        // absent from the current list. Existing edits must never silently move.
        if (!string.IsNullOrWhiteSpace(eventCalendarId)
            && !string.Equals(eventCalendarId, GoogleCalendarDefaults.PrimaryCalendarId, StringComparison.Ordinal))
        {
            return eventCalendarId;
        }

        // "primary" is a Google API alias and is not necessarily the concrete id
        // returned by CalendarList. Prefer a concrete id already selected in the UI.
        if (!string.Equals(EditorCalendarId, GoogleCalendarDefaults.PrimaryCalendarId, StringComparison.Ordinal)
            && AvailableCalendars.Any(item => string.Equals(item.Id, EditorCalendarId, StringComparison.Ordinal)))
        {
            return EditorCalendarId;
        }

        var selected = AvailableCalendars.FirstOrDefault(item => item.IsSelected);
        if (selected is not null)
        {
            return selected.Id;
        }

        if (AvailableCalendars.Count > 0)
        {
            return AvailableCalendars[0].Id;
        }

        return ResolveEditorCalendarId();
    }
}
