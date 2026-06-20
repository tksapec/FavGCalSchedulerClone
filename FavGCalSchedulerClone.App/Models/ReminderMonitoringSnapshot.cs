namespace FavGCalSchedulerClone.App.Models;

public sealed record ReminderMonitoringSnapshot(
    bool IsRunning,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastCheckAt,
    DateTimeOffset? NextCheckAt,
    int StoredEventsCount,
    int ExpandedEventsCount,
    int ReminderConfiguredCount,
    int NoReminderCount,
    int CandidateCount,
    int DueCount,
    int FiredExcludedCount,
    int SnoozedExcludedCount,
    int SucceededCount,
    int FailedCount,
    string? LastError,
    IReadOnlyList<ReminderCandidateDiagnostic> Candidates)
{
    public static ReminderMonitoringSnapshot Stopped { get; } = new(
        false, null, null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, []);
}

public sealed record ReminderCandidateDiagnostic(
    string EventId,
    string Title,
    string OccurrenceKey,
    int? ReminderMinutesBeforeStart,
    DateTimeOffset EventStart,
    DateTimeOffset? RemindAt,
    DateTimeOffset CheckedAt,
    bool IsDue,
    bool IsFired,
    DateTimeOffset? SnoozedUntil,
    string Reason,
    bool? GoogleReminderUseDefault = null,
    string GooglePopupReminderText = "",
    string GoogleEmailReminderText = "",
    string GoogleDefaultReminderText = "",
    int? AdoptedGoogleReminderMinutes = null,
    string ReminderDifferenceText = "",
    string? ErrorMessage = null);
