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

    public string SummaryText =>
        $"送信 {Pushed} / 取得 {Pulled} / スキップ {Skipped} / 競合 {Conflicts} / 失敗 {Failed} / 削除 {Deleted} / 再作成 {Recreated}";
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
    string Detail,
    string? ChangeFields = null,
    IReadOnlyList<SyncFieldDiff>? FieldDiffs = null)
{
    public string ChangeFieldsText => Services.EventDirtyFieldTracker.ToDisplayText(ChangeFields);
}

public sealed record SyncFieldDiff(
    string FieldName,
    string DisplayName,
    string LocalValue,
    string GoogleValue,
    string Direction,
    bool IsDifferent);

public sealed record SyncCalendarDiagnostic(
    string CalendarId,
    bool HasSyncToken,
    int DirtyCount);

public sealed record SyncDirtyItem(
    string LocalId,
    string Kind,
    string CalendarId,
    DateTimeOffset Start,
    string Title,
    string Operation,
    string? GoogleEventId,
    DateTimeOffset UpdatedAt,
    string? FailureReason,
    string? ErrorMessage,
    string? ChangeFields = null)
{
    public string ChangeFieldsText => Services.EventDirtyFieldTracker.ToDisplayText(ChangeFields);

    public SyncDirtyItem(
        string kind,
        string calendarId,
        DateTimeOffset start,
        string title,
        string operation,
        string? googleEventId,
        DateTimeOffset updatedAt)
        : this("", kind, calendarId, start, title, operation, googleEventId, updatedAt, null, null, null)
    {
    }
}

public sealed record SyncFailureDiagnostic(
    DateTimeOffset OccurredAt,
    string Title,
    DateTimeOffset Start,
    string CalendarId,
    string LocalId,
    string? GoogleEventId,
    string Operation,
    string Kind,
    string FailureReason,
    string? HttpStatusCode,
    string? GoogleErrorMessage,
    string? ExceptionMessage,
    string Direction = "Push",
    bool SyncTokenPresent = false,
    string? PageToken = null,
    string? FailureCategory = null);

public sealed record SyncDiagnosticsSnapshot(
    SyncResult? LastResult,
    IReadOnlyList<SyncResult> History,
    IReadOnlyList<SyncCalendarDiagnostic> Calendars,
    int DirtyCount,
    IReadOnlyList<SyncDirtyItem> DirtyItems,
    IReadOnlyList<SyncFailureDiagnostic> Failures);
