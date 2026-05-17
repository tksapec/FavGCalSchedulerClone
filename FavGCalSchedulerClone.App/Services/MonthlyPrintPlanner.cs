using System.Globalization;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public sealed record MonthlyPrintPlan(
    DateTime Month,
    string Title,
    IReadOnlyList<MonthlyPrintDay> Days);

public sealed record MonthlyPrintDay(
    DateTime Date,
    bool IsCurrentMonth,
    bool IsToday,
    bool IsSunday,
    bool IsSaturday,
    IReadOnlyList<MonthlyPrintEntry> Entries,
    int HiddenEntryCount);

public sealed record MonthlyPrintEntry(
    string Text,
    string DisplayColor,
    DateTimeOffset Start,
    bool IsAllDay);

public static class MonthlyPrintPlanner
{
    public const int MaxEntriesPerDay = 4;

    public static MonthlyPrintPlan Create(DateTime month, IEnumerable<CalendarEvent> events)
    {
        var normalizedMonth = new DateTime(month.Year, month.Month, 1);
        var (gridStart, gridEnd) = DateRangeHelper.MonthGridRange(normalizedMonth);
        var visibleEvents = events
            .Where(calendarEvent => !calendarEvent.IsDeleted)
            .OrderBy(calendarEvent => calendarEvent.IsAllDay ? 0 : 1)
            .ThenBy(calendarEvent => calendarEvent.Start)
            .ThenBy(calendarEvent => calendarEvent.Title)
            .ToArray();

        var days = new List<MonthlyPrintDay>(42);
        for (var date = gridStart; date < gridEnd; date = date.AddDays(1))
        {
            var dayEvents = visibleEvents
                .Where(calendarEvent => DateRangeHelper.OccursOn(calendarEvent, date))
                .ToArray();
            var entries = dayEvents
                .Take(MaxEntriesPerDay)
                .Select(calendarEvent => new MonthlyPrintEntry(
                    FormatEntryText(calendarEvent),
                    calendarEvent.DisplayColor,
                    calendarEvent.Start,
                    calendarEvent.IsAllDay))
                .ToArray();

            days.Add(new MonthlyPrintDay(
                date,
                date.Month == normalizedMonth.Month,
                date.Date == DateTime.Today,
                date.DayOfWeek == DayOfWeek.Sunday,
                date.DayOfWeek == DayOfWeek.Saturday,
                entries,
                Math.Max(0, dayEvents.Length - entries.Length)));
        }

        return new MonthlyPrintPlan(
            normalizedMonth,
            $"{normalizedMonth:yyyy}年 {normalizedMonth.Month}月",
            days);
    }

    private static string FormatEntryText(CalendarEvent calendarEvent)
    {
        if (calendarEvent.IsAllDay)
        {
            return calendarEvent.Title;
        }

        return $"{calendarEvent.Start.ToString("HH:mm", CultureInfo.InvariantCulture)} {calendarEvent.Title}";
    }
}
