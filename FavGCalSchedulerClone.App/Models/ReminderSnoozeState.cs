namespace FavGCalSchedulerClone.App.Models;

public sealed class ReminderSnoozeState
{
    public string OccurrenceKey { get; set; } = "";
    public DateTimeOffset SnoozeUntil { get; set; }
}
