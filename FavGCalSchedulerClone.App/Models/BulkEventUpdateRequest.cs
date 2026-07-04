namespace FavGCalSchedulerClone.App.Models;

public sealed record BulkEventUpdateRequest(
    string? CalendarId = null,
    string? ColorId = null,
    int? ReminderMinutesBeforeStart = null,
    bool? AppReminderEnabled = null,
    bool? GoogleEmailReminderEnabled = null,
    bool UpdateColor = false)
{
    public bool UpdatesCalendar => CalendarId is not null;
    public bool UpdatesColor => UpdateColor || ColorId is not null;
    public bool UpdatesReminder =>
        ReminderMinutesBeforeStart is not null
        || AppReminderEnabled is not null
        || GoogleEmailReminderEnabled is not null;
}
