namespace FavGCalSchedulerClone.App.Models;

public sealed record BulkEventOperationResult(
    int SelectedCount,
    int AffectedCount,
    int UnsupportedRecurrenceCount = 0,
    int MissingCount = 0,
    int TodoReminderSkippedCount = 0,
    int UnchangedCount = 0);
