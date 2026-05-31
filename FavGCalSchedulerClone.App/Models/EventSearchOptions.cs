namespace FavGCalSchedulerClone.App.Models;

public enum EventKindFilter
{
    All,
    Schedule,
    Todo
}

public enum EventSearchRange
{
    Day,
    Month,
    Year,
    Custom,
    All
}

public sealed record EventListFilter(
    string Query,
    EventKindFilter KindFilter,
    EventSearchRange Range,
    DateTime ReferenceDate,
    string? CalendarId = null,
    DateTime? StartDate = null,
    DateTime? EndDate = null);
