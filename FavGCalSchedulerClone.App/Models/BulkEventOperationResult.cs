namespace FavGCalSchedulerClone.App.Models;

public sealed record BulkEventOperationResult(
    int SelectedCount,
    int AffectedCount,
    int UnsupportedRecurrenceCount = 0,
    int MissingCount = 0,
    int TodoReminderSkippedCount = 0,
    int UnchangedCount = 0)
{
    public string FormatStatus(string operationLabel, string affectedLabel)
    {
        var parts = new List<string>
        {
            $"{operationLabel}: 選択 {SelectedCount}件",
            $"{affectedLabel} {AffectedCount}件"
        };

        if (UnsupportedRecurrenceCount > 0)
        {
            parts.Add($"繰り返し予定対象外 {UnsupportedRecurrenceCount}件");
        }

        if (MissingCount > 0)
        {
            parts.Add($"未検出 {MissingCount}件");
        }

        if (TodoReminderSkippedCount > 0)
        {
            parts.Add($"ToDo通知対象外 {TodoReminderSkippedCount}件");
        }

        if (UnchangedCount > 0)
        {
            parts.Add($"変更なし {UnchangedCount}件");
        }

        return string.Join(" / ", parts);
    }
}
