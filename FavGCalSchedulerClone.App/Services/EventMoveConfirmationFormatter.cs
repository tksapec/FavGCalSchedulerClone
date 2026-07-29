using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

internal sealed record EventMoveConfirmationRequest(
    string Title, bool IsTodo, bool IsAllDay, DateTimeOffset OriginalStart, DateTimeOffset OriginalEnd,
    DateTime SourceSegmentDate, DateTime TargetDate, RecurrenceEditScope? RecurrenceScope);

internal static class EventMoveConfirmationFormatter
{
    public static string Format(EventMoveConfirmationRequest request)
    {
        var shift = (request.TargetDate.Date - request.SourceSegmentDate.Date).Days;
        var movedStart = request.OriginalStart.AddDays(shift);
        var movedEnd = request.OriginalEnd.AddDays(shift);
        var format = request.IsAllDay ? "yyyy/MM/dd" : "yyyy/MM/dd HH:mm";
        var multiDay = request.OriginalEnd.Date > request.OriginalStart.Date.AddDays(request.IsAllDay ? 1 : 0);
        string Range(DateTimeOffset start, DateTimeOffset end) => multiDay
            ? $"{start.ToString(format)} ～ {end.ToString(format)}"
            : start.ToString(format);
        var direction = shift > 0 ? $"{shift}日後" : $"{Math.Abs(shift)}日前";
        var scope = request.RecurrenceScope switch
        {
            RecurrenceEditScope.ThisOccurrence => "\n\n対象:\nこの予定のみ",
            RecurrenceEditScope.ThisAndFollowing => "\n\n対象:\nこれ以降の予定",
            RecurrenceEditScope.AllEvents => "\n\n対象:\n繰り返し予定全体",
            _ => string.Empty
        };
        return $"「{request.Title}」を移動しますか？\n\n移動元:\n{Range(request.OriginalStart, request.OriginalEnd)}\n\n移動先:\n{Range(movedStart, movedEnd)}\n\n移動日数:\n{direction}{scope}";
    }
}
