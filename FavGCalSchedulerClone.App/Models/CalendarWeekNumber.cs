using System.Globalization;

namespace FavGCalSchedulerClone.App.Models;

public sealed record CalendarWeekNumber(int IsoYear, int WeekNumber, DateTime DisplayRowStart, DateTime IsoReferenceDate)
{
    public string DisplayText => $"{WeekNumber:00}";
    public string ToolTipText => $"{IsoYear}年第{WeekNumber}週";

    public static IReadOnlyList<CalendarWeekNumber> CreateRows(DateTime gridStart, bool weekStartsOnMonday)
    {
        var rows = new List<CalendarWeekNumber>(6);
        for (var row = 0; row < 6; row++)
        {
            var rowStart = gridStart.Date.AddDays(row * 7);
            var referenceDate = weekStartsOnMonday ? rowStart : rowStart.AddDays(1);
            rows.Add(new CalendarWeekNumber(
                ISOWeek.GetYear(referenceDate),
                ISOWeek.GetWeekOfYear(referenceDate),
                rowStart,
                referenceDate));
        }

        return rows;
    }
}
