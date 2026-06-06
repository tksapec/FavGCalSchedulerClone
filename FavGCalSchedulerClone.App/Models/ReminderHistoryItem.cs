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
    public string? DeliveryMethod { get; set; }
    public bool UsedMessageBoxFallback { get; set; }
    public bool ToastVerified { get; set; }
    public string? ToastStatus { get; set; }
    public string? DeliveryError { get; set; }
    public int FailureCount { get; set; }
    public DateTimeOffset? LastFailedAt { get; set; }

    public string KindText => IsTodoLike ? "ToDo" : "予定";
    public string NotifiedAtText => NotifiedAt.ToString("yyyy/MM/dd HH:mm");
    public string SnoozedUntilText => SnoozedUntil is null ? "" : SnoozedUntil.Value.ToString("yyyy/MM/dd HH:mm");
    public string DeliveryStatusText => FormatDeliveryStatus();

    private string FormatDeliveryStatus()
    {
        var status = DeliverySucceeded ? "OK" : FailureCount > 1 ? $"Failed x{FailureCount}" : "Failed";
        var fallback = UsedMessageBoxFallback ? " + MessageBox" : "";
        var verified = ToastVerified ? " verified" : "";
        var detail = string.IsNullOrWhiteSpace(DeliveryError) ? ToastStatus : DeliveryError;
        return string.IsNullOrWhiteSpace(detail)
            ? $"{status} ({DeliveryMethod}{fallback}{verified})"
            : $"{status} ({DeliveryMethod}{fallback}{verified}): {detail}";
    }
}
