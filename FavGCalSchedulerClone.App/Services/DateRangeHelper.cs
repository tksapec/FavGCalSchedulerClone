using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public static class DateRangeHelper
{
    public static bool OccursOn(CalendarEvent calendarEvent, DateTime date)
    {
        var dayStart = new DateTimeOffset(date.Date, calendarEvent.Start.Offset);
        var dayEnd = dayStart.AddDays(1);

        if (calendarEvent.IsAllDay)
        {
            var startDate = calendarEvent.Start.Date;
            var endDateExclusive = calendarEvent.End.Date <= startDate
                ? startDate.AddDays(1)
                : calendarEvent.End.Date;
            return date.Date >= startDate && date.Date < endDateExclusive;
        }

        return calendarEvent.Start < dayEnd && calendarEvent.End > dayStart;
    }

    public static (DateTime Start, DateTime End) MonthGridRange(DateTime month, bool weekStartsOnMonday = false)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var offset = weekStartsOnMonday
            ? ((int)first.DayOfWeek + 6) % 7
            : (int)first.DayOfWeek;
        var start = first.AddDays(-offset);
        return (start, start.AddDays(42));
    }
}
