namespace FavGCalSchedulerClone.App.Models;

public sealed record UndoOperation(
    string Description,
    IReadOnlyList<CalendarEvent> BeforeEvents,
    IReadOnlyList<string> CreatedEventIds);
