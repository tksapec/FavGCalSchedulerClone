using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

internal static class TodoDisplayFilter
{
    public static bool IsWithinDisplayPeriod(CalendarEvent calendarEvent, int months, DateTime today) =>
        months == 0 || calendarEvent.Start.Date >= today.Date.AddMonths(-months);
}
