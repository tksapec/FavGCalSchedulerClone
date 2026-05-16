using System.Text.RegularExpressions;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public static partial class TagService
{
    public static IReadOnlyList<CalendarTag> DefaultTags { get; } =
    [
        new() { Name = "#holiday", Color = "#FCA5A5", Priority = 100 },
        new() { Name = "#workday", Color = "#FFFFFF", Priority = 95 },
        new() { Name = "#important", Color = "#FDE68A", Priority = 90 },
        new() { Name = "#work", Color = "#93C5FD", Priority = 50 },
        new() { Name = "#private", Color = "#C4B5FD", Priority = 40 }
    ];

    public static IReadOnlyList<string> ExtractTags(string? title, string? description)
    {
        var input = $"{title ?? ""} {description ?? ""}";
        return TagRegex()
            .Matches(input)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsHoliday(CalendarEvent calendarEvent) =>
        ExtractTags(calendarEvent.Title, calendarEvent.Description)
            .Any(x => string.Equals(x, "#holiday", StringComparison.OrdinalIgnoreCase));

    public static bool IsWorkday(CalendarEvent calendarEvent) =>
        ExtractTags(calendarEvent.Title, calendarEvent.Description)
            .Any(x => string.Equals(x, "#workday", StringComparison.OrdinalIgnoreCase));

    public static bool HasWorkdayOverride(IEnumerable<CalendarEvent> events, DateTime date) =>
        events.Any(e => IsWorkday(e) && DateRangeHelper.OccursOn(e, date));

    public static bool HasHolidayWithoutWorkdayOverride(IEnumerable<CalendarEvent> events, DateTime date)
    {
        var eventsOnDate = events.Where(e => DateRangeHelper.OccursOn(e, date)).ToArray();
        return eventsOnDate.Any(IsHoliday) && !eventsOnDate.Any(IsWorkday);
    }

    public static bool IsTodoLike(CalendarEvent calendarEvent) =>
        TodoRegex().IsMatch($"{calendarEvent.Title} {calendarEvent.Description}");

    public static CalendarTag? FindDisplayTag(CalendarEvent calendarEvent, IEnumerable<CalendarTag> tags)
    {
        var eventTags = ExtractTags(calendarEvent.Title, calendarEvent.Description);
        return tags
            .Where(t => eventTags.Any(et => string.Equals(et, t.Name, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(t => t.Priority)
            .FirstOrDefault();
    }

    [GeneratedRegex(@"#[\p{L}\p{N}_%-]+", RegexOptions.Compiled)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"#todo[A-Fa-f]?\d{1,3}%", RegexOptions.Compiled)]
    private static partial Regex TodoRegex();
}
