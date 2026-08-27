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

        // "primary" is a Google API alias and is not necessarily the concrete id
        // returned by CalendarList. Prefer a registered selected calendar so the
        // editor ComboBox always has a matching item.
        if (!string.IsNullOrWhiteSpace(eventCalendarId)
            && !string.Equals(eventCalendarId, GoogleCalendarDefaults.PrimaryCalendarId, StringComparison.Ordinal))
        {
            // Preserve a non-empty historical/source calendar that is temporarily
            // absent from the current list. Existing edits must never silently move.
            return eventCalendarId;
        }

        return ResolveEditorCalendarId();
    }
}
