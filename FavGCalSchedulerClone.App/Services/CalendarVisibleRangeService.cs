using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

internal static class CalendarVisibleRangeService
{
    public static DateTime GetWeekStart(DateTime anchorDate, bool weekStartsOnMonday)
    {
        var offset = weekStartsOnMonday
            ? ((int)anchorDate.DayOfWeek + 6) % 7
            : (int)anchorDate.DayOfWeek;
        return anchorDate.Date.AddDays(-offset);
    }

    public static IEnumerable<DateTime> GetVisibleDates(CalendarViewMode viewMode, IEnumerable<CalendarDay> monthDays, DateTime anchorDate, bool weekStartsOnMonday)
    {
        return viewMode switch
        {
            CalendarViewMode.Month => monthDays.Select(day => day.Date),
            CalendarViewMode.Week => Enumerable.Range(0, 7).Select(offset => GetWeekStart(anchorDate, weekStartsOnMonday).AddDays(offset)),
            CalendarViewMode.Day => [anchorDate.Date],
            _ => monthDays.Select(day => day.Date)
        };
    }
}
