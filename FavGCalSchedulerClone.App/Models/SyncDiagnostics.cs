namespace FavGCalSchedulerClone.App.Models;

public sealed record SyncResult(
    int Pushed,
    int Pulled,
    int Skipped,
    int Conflicts,
    int Failed,
    int Deleted,
    int Recreated,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string Message)
{
    public static SyncResult Empty(string message) =>
        new(0, 0, 0, 0, 0, 0, 0, DateTimeOffset.Now, DateTimeOffset.Now, message);
}

public sealed record SyncPreview(
    DateTimeOffset CreatedAt,
    IReadOnlyList<SyncPreviewItem> PushItems,
    IReadOnlyList<SyncPreviewItem> PullItems,
    IReadOnlyList<SyncPreviewItem> DeleteItems,
    IReadOnlyList<SyncPreviewItem> ConflictItems,
    IReadOnlyList<SyncPreviewItem> ErrorItems,
    IReadOnlyList<SyncCalendarDiagnostic> Calendars)
{
    public int PushCount => PushItems.Count;
    public int PullCount => PullItems.Count;
    public int DeleteCount => DeleteItems.Count;
    public int ConflictCount => ConflictItems.Count;
    public int ErrorCount => ErrorItems.Count;
}

public sealed record SyncPreviewItem(
    string CalendarId,
    string? LocalId,
    string? GoogleEventId,
    string Title,
    DateTimeOffset? Start,
    string Kind,
    string Detail);

public sealed record SyncCalendarDiagnostic(
    string CalendarId,
    bool HasSyncToken,
    int DirtyCount);

public sealed record SyncDiagnosticsSnapshot(
    SyncResult? LastResult,
    IReadOnlyList<SyncResult> History,
    IReadOnlyList<SyncCalendarDiagnostic> Calendars,
    int DirtyCount);
