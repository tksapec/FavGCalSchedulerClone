using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

internal static class AppSettingsNormalizer
{
    private static readonly int[] ValidTodoMonthValues = [0, 1, 3, 6, 12];
    private static readonly int[] ValidAutomaticSyncIntervals = [30, 60, 120, 360];

    public static AppSettings Normalize(AppSettings settings)
    {
        settings.StartupTabIndex = NormalizeTabIndex(settings.StartupTabIndex);
        settings.StartupTodoTabIndex = Math.Clamp(settings.StartupTodoTabIndex, 0, 1);
        settings.CalendarLabelFontSizeIndex = Math.Clamp(settings.CalendarLabelFontSizeIndex, 1, 3);
        settings.SideListFontSizeIndex = Math.Clamp(settings.SideListFontSizeIndex, 1, 3);
        settings.WindowOpacity = Math.Clamp(settings.WindowOpacity, 64, 255);
        settings.ReminderSoundVolume = Math.Clamp(settings.ReminderSoundVolume, 0, 100);
        settings.AdoptGoogleEmailRemindersAsLocalNotifications = false;
        settings.IncompleteTodoDisplayPeriodMonths = NormalizeTodoMonths(settings.IncompleteTodoDisplayPeriodMonths);
        settings.CompletedTodoDisplayPeriodMonths = NormalizeTodoMonths(settings.CompletedTodoDisplayPeriodMonths);
        settings.AutomaticSyncIntervalMinutes = settings.AutomaticSyncIntervalMinutes is int interval
            && ValidAutomaticSyncIntervals.Contains(interval)
                ? interval
                : null;
        settings.VisibleCalendarIds = settings.VisibleCalendarIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        settings.EventColorSettings = settings.EventColorSettings
            .Where(setting => !string.IsNullOrWhiteSpace(setting.ColorId))
            .GroupBy(setting => setting.ColorId.Trim(), StringComparer.Ordinal)
            .Select(group =>
            {
                var setting = group.Last();
                return new EventColorSetting
                {
                    ColorId = group.Key,
                    Label = string.IsNullOrWhiteSpace(setting.Label) ? null : setting.Label.Trim(),
                    IsEnabled = setting.IsEnabled
                };
            })
            .Where(setting => int.TryParse(setting.ColorId, out var id) && id is >= 1 and <= 11)
            .OrderBy(setting => int.Parse(setting.ColorId))
            .ToList();
        if (settings.VisibleCalendarIds.Count == 0)
        {
            settings.VisibleCalendarIds.Add(string.IsNullOrWhiteSpace(settings.ActiveCalendarId)
                ? GoogleCalendarDefaults.PrimaryCalendarId
                : settings.ActiveCalendarId);
        }

        settings.ActiveCalendarId = string.IsNullOrWhiteSpace(settings.ActiveCalendarId)
            ? settings.VisibleCalendarIds[0]
            : settings.ActiveCalendarId;
        return settings;
    }

    public static int NormalizeTodoMonths(int months) =>
        ValidTodoMonthValues.Contains(months) ? months : 0;

    public static int NormalizeTabIndex(int tabIndex) => Math.Clamp(tabIndex, 0, 4);
}
