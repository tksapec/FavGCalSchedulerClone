namespace FavGCalSchedulerClone.App.Models;

public sealed class ReminderHistoryItem
{
    public string OccurrenceKey { get; set; } = "";
    public string EventId { get; set; } = "";
    public string Title { get; set; } = "";
    public string DateDisplayText { get; set; } = "";
    public DateTimeOffset NotifiedAt { get; set; }
    public DateTimeOffset RemindAt { get; set; }
    public DateTimeOffset EventStart { get; set; }
    public DateTimeOffset OccurrenceStart { get; set; }
    public string CalendarId { get; set; } = GoogleCalendarDefaults.PrimaryCalendarId;
    public bool IsTodoLike { get; set; }
    public DateTimeOffset? SnoozedUntil { get; set; }
    public bool DeliverySucceeded { get; set; } = true;
    public string? DeliveryError { get; set; }

    public string KindText => IsTodoLike ? "ToDo" : "予定";
    public string NotifiedAtText => NotifiedAt.ToString("yyyy/MM/dd HH:mm");
    public string SnoozedUntilText => SnoozedUntil is null ? "" : SnoozedUntil.Value.ToString("yyyy/MM/dd HH:mm");
    public string DeliveryStatusText => DeliverySucceeded ? "OK" : $"Failed: {DeliveryError}";
}
