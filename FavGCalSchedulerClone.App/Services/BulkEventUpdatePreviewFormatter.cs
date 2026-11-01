using System.Text;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public static class BulkEventUpdatePreviewFormatter
{
    public static string Format(
        BulkEventUpdateRequest update,
        int targetCount,
        IReadOnlyList<GoogleCalendarSelectionItem> calendars)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(calendars);

        var lines = new List<string> { $"対象: {targetCount}件" };
        if (update.UpdatesCalendar)
        {
            var destination = calendars.FirstOrDefault(item => string.Equals(item.Id, update.CalendarId, StringComparison.Ordinal));
            lines.Add(destination is null
                ? $"変更先カレンダー: {update.CalendarId}"
                : $"変更先カレンダー: {destination.Summary} ({destination.Id})");
        }

        if (update.UpdatesColor)
        {
            lines.Add($"色: {(string.IsNullOrWhiteSpace(update.ColorId) ? "既定色" : $"ID {update.ColorId}")}");
        }

        if (update.UpdatesReminder)
        {
            var time = update.ReminderMinutesBeforeStart switch
            {
                null => "通知なし",
                0 => "開始時刻",
                int minutes => $"{minutes}分前"
            };
            var app = update.AppReminderEnabled == true ? time : "なし";
            var email = update.GoogleEmailReminderEnabled == true ? time : "なし";
            lines.Add($"通知: アプリ内 {app} / Googleメール {email}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
