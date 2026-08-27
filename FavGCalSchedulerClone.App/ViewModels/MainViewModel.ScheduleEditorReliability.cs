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

        string activeCalendarId;
        lock (_settingsStateLock)
        {
            activeCalendarId = _settings.ActiveCalendarId;
        }

        // ActiveCalendarId is the persisted default calendar. Selecting an existing
        // event also changes EditorCalendarId, so do not let that transient editor
        // state replace the configured default for a later new schedule. A stale
        // active id that is no longer visible must not make a newly saved event vanish.
        if (!string.IsNullOrWhiteSpace(activeCalendarId)
            && AvailableCalendars.Any(item => item.IsSelected
                && string.Equals(item.Id, activeCalendarId, StringComparison.Ordinal)))
        {
            return activeCalendarId;
        }

        var selected = AvailableCalendars.FirstOrDefault(item => item.IsSelected);
        if (selected is not null)
        {
            return selected.Id;
        }

        if (!string.Equals(EditorCalendarId, GoogleCalendarDefaults.PrimaryCalendarId, StringComparison.Ordinal)
            && AvailableCalendars.Any(item => string.Equals(item.Id, EditorCalendarId, StringComparison.Ordinal)))
        {
            return EditorCalendarId;
        }

        if (AvailableCalendars.Count > 0)
        {
            return AvailableCalendars[0].Id;
        }

        return string.IsNullOrWhiteSpace(activeCalendarId)
            ? ResolveEditorCalendarId()
            : activeCalendarId;
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
