namespace FavGCalSchedulerClone.App.Models;

public sealed class GoogleReminderMetadata
{
    public bool? UseDefault { get; set; }
    public List<int> PopupMinutes { get; set; } = [];
    public List<int> EmailMinutes { get; set; } = [];
    public List<int> DefaultPopupMinutes { get; set; } = [];
    public List<int> DefaultEmailMinutes { get; set; } = [];
    public int? AdoptedReminderMinutes { get; set; }
    public string? AdoptedReminderMethod { get; set; }
    public string? Source { get; set; }

    public bool HasGoogleReminder =>
        UseDefault == true
        || PopupMinutes.Count > 0
        || EmailMinutes.Count > 0
        || DefaultPopupMinutes.Count > 0
        || DefaultEmailMinutes.Count > 0;

    public bool HasEmailOnly =>
        PopupMinutes.Count == 0
        && DefaultPopupMinutes.Count == 0
        && (EmailMinutes.Count > 0 || DefaultEmailMinutes.Count > 0);

    public GoogleReminderMetadata Clone() => new()
    {
        UseDefault = UseDefault,
        PopupMinutes = [.. PopupMinutes],
        EmailMinutes = [.. EmailMinutes],
        DefaultPopupMinutes = [.. DefaultPopupMinutes],
        DefaultEmailMinutes = [.. DefaultEmailMinutes],
        AdoptedReminderMinutes = AdoptedReminderMinutes,
        AdoptedReminderMethod = AdoptedReminderMethod,
        Source = Source
    };
}
