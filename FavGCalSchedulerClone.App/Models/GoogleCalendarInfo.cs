namespace FavGCalSchedulerClone.App.Models;

public sealed record GoogleCalendarInfo(
    string Id,
    string Summary,
    IReadOnlyList<GoogleReminderOverride>? DefaultReminders = null);

public sealed record GoogleReminderOverride(string Method, int Minutes);
