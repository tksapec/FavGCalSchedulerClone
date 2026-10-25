using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class EventListSelectionRestore
{
    public static IReadOnlyList<CalendarEvent> GetRetainedSelection(
        IEnumerable<string> selectedIds,
        IEnumerable<CalendarEvent> refreshedEvents)
    {
        var selectedIdSet = selectedIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        return refreshedEvents
            .Where(calendarEvent => selectedIdSet.Contains(calendarEvent.Id))
            .ToArray();
    }
}
