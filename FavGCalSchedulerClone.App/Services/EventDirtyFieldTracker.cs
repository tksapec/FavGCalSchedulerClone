using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

internal static class EventDirtyFieldTracker
{
    private static readonly string[] FieldOrder =
    [
        "New", "Deleted", "Title", "Description", "Location", "StartEnd", "AllDay",
        "Reminder", "Color", "Calendar", "Recurrence", "Unknown"
    ];

    public static string Merge(string? existingFields, CalendarEvent? existing, CalendarEvent current)
    {
        var fields = Parse(existingFields);
        if (existing is null)
        {
            fields.Add("New");
        }
        else
        {
            AddIfChanged(fields, "Deleted", existing.IsDeleted, current.IsDeleted);
            AddIfChanged(fields, "Title", existing.Title, current.Title);
            AddIfChanged(fields, "Description", Normalize(existing.Description), Normalize(current.Description));
            AddIfChanged(fields, "Location", Normalize(existing.Location), Normalize(current.Location));
            if (existing.Start != current.Start || existing.End != current.End) fields.Add("StartEnd");
            AddIfChanged(fields, "AllDay", existing.IsAllDay, current.IsAllDay);
            AddIfChanged(fields, "Reminder", existing.ReminderMinutesBeforeStart, current.ReminderMinutesBeforeStart);
            AddIfChanged(fields, "Reminder", string.Join("|", existing.EffectiveAppReminderMinutesBeforeStart), string.Join("|", current.EffectiveAppReminderMinutesBeforeStart));
            AddIfChanged(fields, "Reminder", string.Join("|", existing.EffectiveGoogleEmailReminderMinutesBeforeStart), string.Join("|", current.EffectiveGoogleEmailReminderMinutesBeforeStart));
            AddIfChanged(fields, "Reminder", existing.IsAppReminderEnabled, current.IsAppReminderEnabled);
            AddIfChanged(fields, "Reminder", existing.IsGoogleEmailReminderEnabled, current.IsGoogleEmailReminderEnabled);
            AddIfChanged(fields, "Color", Normalize(existing.ColorId), Normalize(current.ColorId));
            AddIfChanged(fields, "Calendar", existing.CalendarId, current.CalendarId);
            AddIfChanged(fields, "Recurrence", Normalize(existing.RecurrenceJson), Normalize(current.RecurrenceJson));
        }

        if (current.IsDirty && fields.Count == 0) fields.Add("Unknown");
        return string.Join(",", FieldOrder.Where(fields.Contains));
    }

    public static string ToDisplayText(string? fields)
    {
        var parsed = Parse(fields);
        return string.Join("、", FieldOrder.Where(parsed.Contains).Select(field => field switch
        {
            "New" => "新規", "Deleted" => "削除", "Title" => "件名変更", "Description" => "内容変更",
            "Location" => "場所変更", "StartEnd" => "日時変更", "AllDay" => "終日変更",
            "Reminder" => "通知変更", "Color" => "色変更", "Calendar" => "カレンダー変更",
            "Recurrence" => "繰り返し変更", _ => "変更内容不明"
        }));
    }

    private static HashSet<string> Parse(string? value) => string.IsNullOrWhiteSpace(value)
        ? new HashSet<string>(StringComparer.Ordinal)
        : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);

    private static void AddIfChanged<T>(ISet<string> fields, string field, T before, T after)
    {
        if (!EqualityComparer<T>.Default.Equals(before, after)) fields.Add(field);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
