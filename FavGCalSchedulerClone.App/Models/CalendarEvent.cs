namespace FavGCalSchedulerClone.App.Models;

public sealed class CalendarEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? GoogleEventId { get; set; }
    public string? RecurringEventId { get; set; }
    public string? RecurringParentId { get; set; }
    public DateTimeOffset? OriginalStart { get; set; }
    public bool IsRecurrenceException { get; set; }
    public string CalendarId { get; set; } = GoogleCalendarDefaults.PrimaryCalendarId;
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Location { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public bool IsAllDay { get; set; }
    public string? ColorId { get; set; }
    public string? RecurrenceJson { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastSyncedAt { get; set; }
    public bool IsDirty { get; set; } = true;
    public bool IsTodoLike { get; set; }
    public string DisplayColor { get; set; } = "#E5E7EB";
    public bool IsGeneratedOccurrence { get; set; }

    public string SearchText => $"{Title} {Description} {Location}".Trim();
    public string CalendarDisplayText => IsAllDay ? Title : $"{Start:HH:mm} {Title}";
    public string DateDisplayText => IsAllDay ? Start.ToString("yyyy/MM/dd") : Start.ToString("yyyy/MM/dd HH:mm");
    public TodoMetadata? TodoMetadata => FavGCalSchedulerClone.App.Services.TagService.GetTodoMetadata(this);
    public string TodoPriority => TodoMetadata?.Priority ?? "";
    public int TodoProgress => TodoMetadata?.Progress ?? 0;
    public string TodoProgressText => TodoMetadata?.ProgressText ?? "";
    public bool IsTodoDone => TodoMetadata?.IsDone == true;
    public bool IsRecurringMaster => !string.IsNullOrWhiteSpace(RecurrenceJson) && !IsRecurrenceException;
    public bool IsRecurringSeriesItem => IsRecurringMaster || IsRecurrenceException || IsGeneratedOccurrence || !string.IsNullOrWhiteSpace(RecurringEventId) || !string.IsNullOrWhiteSpace(RecurringParentId);
}
