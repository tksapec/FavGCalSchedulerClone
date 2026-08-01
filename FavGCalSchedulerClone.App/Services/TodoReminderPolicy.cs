using FavGCalSchedulerClone.App.Models;
using Google.Apis.Calendar.v3.Data;

namespace FavGCalSchedulerClone.App.Services;

internal static class TodoReminderPolicy
{
    public static TimeSpan LocalNotificationTime { get; } = new(8, 15, 0);
    public const string OccurrenceKeySuffix = "todo-fixed-0815";

    public static DateTimeOffset GetReminderTime(DateTime dueDate)
    {
        var local = DateTime.SpecifyKind(dueDate.Date.Add(LocalNotificationTime), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    public static DateTimeOffset GetDueDayEnd(DateTime dueDate)
    {
        var local = DateTime.SpecifyKind(dueDate.Date.AddDays(1), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    public static string BuildOccurrenceKey(CalendarEvent todo)
    {
        var anchor = todo.OriginalStart ?? todo.Start;
        var seriesKey = todo.RecurringParentId ?? todo.RecurringEventId ?? todo.Id;
        return $"{seriesKey}:{anchor.UtcTicks}:{OccurrenceKeySuffix}";
    }

    public static void NormalizeLocalFields(CalendarEvent todo)
    {
        todo.ReminderMinutesBeforeStart = null;
        todo.AppReminderMinutesBeforeStart = [];
        todo.GoogleEmailReminderMinutesBeforeStart = [];
        todo.IsAppReminderEnabled = false;
        todo.IsGoogleEmailReminderEnabled = false;
        todo.GoogleReminderMetadata = null;
    }

    public static Event.RemindersData CreateGoogleRemindersDisabled() => new()
    {
        UseDefault = false,
        Overrides = []
    };

    public static bool HasGoogleReminders(Event googleEvent) =>
        googleEvent.Reminders?.UseDefault == true || googleEvent.Reminders?.Overrides?.Count > 0;

    public static bool RequiresLocalCleanup(CalendarEvent todo) =>
        todo.IsTodoLike && !todo.IsDeleted && !string.IsNullOrWhiteSpace(todo.GoogleEventId)
        && (todo.ReminderMinutesBeforeStart is not null
            || todo.AppReminderMinutesBeforeStart.Count > 0
            || todo.GoogleEmailReminderMinutesBeforeStart.Count > 0
            || todo.IsAppReminderEnabled
            || todo.IsGoogleEmailReminderEnabled
            || todo.GoogleReminderMetadata?.HasEffectiveGoogleReminder == true);

    public static CalendarEvent CloneForSyncPlanning(CalendarEvent source) => new()
    {
        Id = source.Id, GoogleEventId = source.GoogleEventId, LastSyncedGoogleEtag = source.LastSyncedGoogleEtag,
        RecurringEventId = source.RecurringEventId, RecurringParentId = source.RecurringParentId,
        OriginalStart = source.OriginalStart, IsRecurrenceException = source.IsRecurrenceException,
        CalendarId = source.CalendarId, Title = source.Title, Description = source.Description,
        Location = source.Location, Start = source.Start, End = source.End, IsAllDay = source.IsAllDay,
        ColorId = source.ColorId, RecurrenceJson = source.RecurrenceJson, IsDeleted = source.IsDeleted,
        UpdatedAt = source.UpdatedAt, LastSyncedAt = source.LastSyncedAt, IsDirty = source.IsDirty,
        DirtyFields = source.DirtyFields, IsTodoLike = source.IsTodoLike,
        ReminderMinutesBeforeStart = source.ReminderMinutesBeforeStart,
        AppReminderMinutesBeforeStart = [.. source.AppReminderMinutesBeforeStart],
        GoogleEmailReminderMinutesBeforeStart = [.. source.GoogleEmailReminderMinutesBeforeStart],
        IsAppReminderEnabled = source.IsAppReminderEnabled,
        IsGoogleEmailReminderEnabled = source.IsGoogleEmailReminderEnabled,
        GoogleReminderMetadata = source.GoogleReminderMetadata?.Clone(), IsGeneratedOccurrence = source.IsGeneratedOccurrence
    };
}
