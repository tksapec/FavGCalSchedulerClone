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
        // returned by CalendarList. New events need a concrete registered id so the
        // editor ComboBox has an actual item selected.
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

    internal IReadOnlyList<GoogleCalendarSelectionItem> CreateScheduleEditorCalendarOptions(
        CalendarEvent? editingEvent,
        string editorCalendarId)
    {
        // Take a snapshot so an automatic calendar-list refresh cannot clear or
        // replace the ComboBox items while a modal editor is open.
        var options = AvailableCalendars.ToList();
        if (string.IsNullOrWhiteSpace(editorCalendarId)
            || options.Any(item => string.Equals(item.Id, editorCalendarId, StringComparison.Ordinal)))
        {
            return options;
        }

        var summary = string.Equals(editorCalendarId, GoogleCalendarDefaults.PrimaryCalendarId, StringComparison.Ordinal)
            ? "メインカレンダー"
            : editingEvent is not null
                && string.Equals(editingEvent.CalendarId, editorCalendarId, StringComparison.Ordinal)
                    ? $"現在のカレンダー ({editorCalendarId})"
                    : editorCalendarId;
        options.Insert(0, new GoogleCalendarSelectionItem
        {
            Id = editorCalendarId,
            Summary = summary
        });
        return options;
    }
}
