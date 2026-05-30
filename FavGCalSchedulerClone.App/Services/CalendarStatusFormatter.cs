using System.Globalization;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

internal static class CalendarStatusFormatter
{
    public static string FormatJapaneseMonthTitle(DateTime date) =>
        $"{date:yyyy}年（{FormatJapaneseEra(date)}） {date.Month}月";

    public static string FormatCalendarStatus(DateTime date)
    {
        var weekOfMonth = ((date.Day - 1) / 7) + 1;
        var elapsedDays = date.DayOfYear;
        var weekOfYear = ((elapsedDays - 1) / 7) + 1;
        var dayOfWeek = date.ToString("dddd", new CultureInfo("ja-JP"));
        return $"{date:yyyy}年({FormatJapaneseEra(date)}){date:MM月dd日} 第{weekOfMonth}{dayOfWeek} {weekOfYear}週目 経過日数 {elapsedDays}日";
    }

    public static string FormatWeekTitle(DateTime date, bool weekStartsOnMonday)
    {
        var offset = weekStartsOnMonday
            ? ((int)date.DayOfWeek + 6) % 7
            : (int)date.DayOfWeek;
        var start = date.Date.AddDays(-offset);
        var end = start.AddDays(6);
        return $"{start:yyyy/M/d} - {end:yyyy/M/d}";
    }

    public static string FormatDayTitle(DateTime date) =>
        date.ToString("yyyy/M/d (ddd)", new CultureInfo("ja-JP"));

    public static IReadOnlyList<string> CreateWeekdayHeaders(WeekdayDisplayType displayType, bool weekStartsOnMonday)
    {
        var headers = displayType switch
        {
            WeekdayDisplayType.EnglishFull => new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
            WeekdayDisplayType.JapaneseShort => new[] { "日", "月", "火", "水", "木", "金", "土" },
            _ => new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }
        };

        return weekStartsOnMonday
            ? headers.Skip(1).Concat(headers.Take(1)).ToArray()
            : headers;
    }

    private static string FormatJapaneseEra(DateTime date)
    {
        var culture = new CultureInfo("ja-JP", false);
        culture.DateTimeFormat.Calendar = new JapaneseCalendar();
        var eraName = culture.DateTimeFormat.GetEraName(culture.DateTimeFormat.Calendar.GetEra(date));
        var year = culture.DateTimeFormat.Calendar.GetYear(date);
        return $"{eraName}{year}年";
    }
}
