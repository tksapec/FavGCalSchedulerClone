using FavGCalSchedulerClone.App.Services;

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
    public MessageBoxNotificationRole MessageBoxRole { get; set; } = MessageBoxNotificationRole.None;
    public bool ToastVerified { get; set; }
    public string? ToastStatus { get; set; }
    public ReminderSoundStatus SoundStatus { get; set; } = ReminderSoundStatus.NotConfigured;
    public string? SoundError { get; set; }
    public string? DeliveryError { get; set; }
    public int FailureCount { get; set; }
    public DateTimeOffset? LastFailedAt { get; set; }

    public string KindText => IsTodoLike ? "ToDo" : "予定";
    public string NotifiedAtText => NotifiedAt.ToString("yyyy/MM/dd HH:mm");
    public string SnoozedUntilText => SnoozedUntil is null ? "" : SnoozedUntil.Value.ToString("yyyy/MM/dd HH:mm");
    public string DeliverySucceededText => DeliverySucceeded ? "成功" : FailureCount > 1 ? $"失敗 x{FailureCount}" : "失敗";
    public string DeliveryMethodText => DeliveryMethod ?? "";
    public string MessageBoxRoleText => MessageBoxRole switch
    {
        MessageBoxNotificationRole.Primary => "MessageBox通知",
        MessageBoxNotificationRole.AfterToast => "MessageBox併用",
        MessageBoxNotificationRole.Fallback => "MessageBoxフォールバック",
        _ => ""
    };
    public string ToastStatusText => string.IsNullOrWhiteSpace(ToastStatus) ? "" : ToastStatus;
    public string SoundStatusText => SoundStatus switch
    {
        ReminderSoundStatus.Played => "再生成功",
        ReminderSoundStatus.MissingFile => string.IsNullOrWhiteSpace(SoundError) ? "ファイルなし" : $"ファイルなし: {SoundError}",
        ReminderSoundStatus.Failed => string.IsNullOrWhiteSpace(SoundError) ? "再生失敗" : $"再生失敗: {SoundError}",
        _ => ""
    };
    public string ErrorText => DeliveryError ?? "";
    public string DeliveryStatusText => FormatDeliveryStatus();

    private string FormatDeliveryStatus()
    {
        var status = DeliverySucceeded ? "OK" : FailureCount > 1 ? $"Failed x{FailureCount}" : "Failed";
        var fallback = MessageBoxRoleText.Length > 0 ? $" / {MessageBoxRoleText}" : UsedMessageBoxFallback ? " / MessageBoxフォールバック" : "";
        var verified = ToastVerified ? " verified" : "";
        var sound = SoundStatusText.Length > 0 ? $" / 音: {SoundStatusText}" : "";
        var detail = string.IsNullOrWhiteSpace(DeliveryError) ? ToastStatus : DeliveryError;
        return string.IsNullOrWhiteSpace(detail)
            ? $"{status} ({DeliveryMethod}{fallback}{verified}{sound})"
            : $"{status} ({DeliveryMethod}{fallback}{verified}{sound}): {detail}";
    }
}
