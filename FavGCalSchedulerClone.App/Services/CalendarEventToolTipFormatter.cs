using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public static class CalendarEventToolTipFormatter
{
    private const int MaxLineLength = 120;
    private const int MaxValueLines = 5;

    public static string Format(CalendarEvent calendarEvent, string? calendarName = null)
    {
        var lines = new List<string>
        {
            "カレンダー",
            string.IsNullOrWhiteSpace(calendarName) ? calendarEvent.CalendarId : calendarName,
            "日時",
            FormatDate(calendarEvent),
            "件名",
            calendarEvent.Title
        };

        AddValue(lines, "場所", calendarEvent.Location);
        AddValue(lines, "内容", calendarEvent.Description);
        if (calendarEvent.IsAppReminderEnabled && calendarEvent.ReminderMinutesBeforeStart is int minutes)
        {
            lines.Add("アプリ内通知");
            lines.Add(minutes == 0 ? "開始時刻" : $"{minutes}分前");
        }

        if (calendarEvent.IsGoogleEmailReminderEnabled)
        {
            lines.Add("Googleメール通知");
            lines.Add(calendarEvent.ReminderMinutesBeforeStart is int emailMinutes
                ? (emailMinutes == 0 ? "開始時刻" : $"{emailMinutes}分前")
                : GoogleReminderDisplayFormatter.FormatEmailReminderText(calendarEvent.GoogleReminderMetadata));
        }

        if (calendarEvent.IsTodoLike)
        {
            lines.Add("ToDo");
            lines.Add($"優先度 {calendarEvent.TodoPriorityDisplayText} / 進捗 {calendarEvent.TodoProgress}%");
        }

        return string.Join(Environment.NewLine, lines.Select(TrimLine));
    }

    private static string FormatDate(CalendarEvent calendarEvent)
    {
        if (!calendarEvent.IsAllDay)
        {
            return $"{calendarEvent.Start:yyyy/MM/dd HH:mm} - {calendarEvent.End:yyyy/MM/dd HH:mm}";
        }

        var lastDate = calendarEvent.End.Date > calendarEvent.Start.Date
            ? calendarEvent.End.Date.AddDays(-1)
            : calendarEvent.Start.Date;
        return lastDate == calendarEvent.Start.Date
            ? $"{calendarEvent.Start:yyyy/MM/dd} (終日)"
            : $"{calendarEvent.Start:yyyy/MM/dd} - {lastDate:yyyy/MM/dd} (終日)";
    }

    private static void AddValue(ICollection<string> lines, string header, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(header);
            lines.Add(TrimValue(value));
        }
    }

    private static string TrimValue(string value)
    {
        var normalizedLines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Take(MaxValueLines + 1)
            .ToArray();
        var visible = normalizedLines.Take(MaxValueLines).Select(TrimLine).ToList();
        if (normalizedLines.Length > MaxValueLines)
        {
            visible.Add("...");
        }

        return string.Join(Environment.NewLine, visible);
    }

    private static string TrimLine(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= MaxLineLength ? trimmed : trimmed[..MaxLineLength] + "...";
    }

}
