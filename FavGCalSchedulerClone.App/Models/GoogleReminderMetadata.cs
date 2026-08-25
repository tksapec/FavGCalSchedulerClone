namespace FavGCalSchedulerClone.App.Models;

public sealed class GoogleReminderMetadata
{
    private List<int> _popupMinutes = [];
    private List<int> _emailMinutes = [];
    private List<int> _defaultPopupMinutes = [];
    private List<int> _defaultEmailMinutes = [];

    public bool? UseDefault { get; set; }
    public List<int> PopupMinutes
    {
        get => _popupMinutes;
        set => _popupMinutes = value ?? [];
    }

    public List<int> EmailMinutes
    {
        get => _emailMinutes;
        set => _emailMinutes = value ?? [];
    }

    public List<int> DefaultPopupMinutes
    {
        get => _defaultPopupMinutes;
        set => _defaultPopupMinutes = value ?? [];
    }

    public List<int> DefaultEmailMinutes
    {
        get => _defaultEmailMinutes;
        set => _defaultEmailMinutes = value ?? [];
    }

    public int? AdoptedReminderMinutes { get; set; }
    public string? AdoptedReminderMethod { get; set; }
    public string? Source { get; set; }

    // This object is the existing persisted Google-event metadata envelope. Keeping
    // source time-zone IDs here avoids a destructive SQLite migration while allowing
    // old databases to gain lossless Google time-zone round-tripping immediately.
    public string? StartTimeZoneId { get; set; }
    public string? EndTimeZoneId { get; set; }

    public bool HasGoogleReminder =>
        UseDefault == true
        || PopupMinutes.Count > 0
        || EmailMinutes.Count > 0
        || DefaultPopupMinutes.Count > 0
        || DefaultEmailMinutes.Count > 0;

    public bool HasEffectiveGoogleReminder =>
        UseDefault == true
            ? DefaultPopupMinutes.Count > 0
              || DefaultEmailMinutes.Count > 0
              || string.Equals(Source, "default-unavailable", StringComparison.Ordinal)
            : PopupMinutes.Count > 0 || EmailMinutes.Count > 0;

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
        Source = Source,
        StartTimeZoneId = StartTimeZoneId,
        EndTimeZoneId = EndTimeZoneId
    };
}
