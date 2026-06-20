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
    public string? DirtyFields { get; set; }
    public bool IsTodoLike { get; set; }
    public int? ReminderMinutesBeforeStart { get; set; }
    public GoogleReminderMetadata? GoogleReminderMetadata { get; set; }
    public string DisplayColor { get; set; } = "#FFFFFF";
    public string DisplayForegroundColor { get; set; } = "#111827";
    public string ToolTipText { get; set; } = "";
    public bool IsGeneratedOccurrence { get; set; }

    public string SearchText => $"{Title} {Description} {Location}".Trim();
    public string CalendarDisplayText => IsAllDay ? Title : $"{Start:HH:mm} {Title}";
    public string CalendarCellDisplayText
    {
        get
        {
            if (!IsTodoLike)
            {
                return CalendarDisplayText;
            }

            return Title;
        }
    }

    public string DateDisplayText => IsAllDay ? Start.ToString("yyyy/MM/dd") : Start.ToString("yyyy/MM/dd HH:mm");
    public string ListStartText => IsAllDay ? $"{Start:yyyy/MM/dd} [終日]" : Start.ToString("yyyy/MM/dd HH:mm");
    public string ListEndText => IsAllDay ? $"{End:yyyy/MM/dd} [終日]" : End.ToString("yyyy/MM/dd HH:mm");
    public string ReminderDisplayText => ReminderMinutesBeforeStart switch
    {
        null => "",
        0 => "時刻",
        < 60 => $"{ReminderMinutesBeforeStart}分前",
        _ when ReminderMinutesBeforeStart % 60 == 0 => $"{ReminderMinutesBeforeStart / 60}時間前",
        _ => $"{ReminderMinutesBeforeStart}分前"
    };
    public string DescriptionPreview => SingleLine(Description);
    public string SummaryDisplayText => IsAllDay
        ? $"{Start:yyyy年MM月dd日(00:00)}〜{End:yyyy年MM月dd日(00:00)}の予定。"
        : $"{Start:yyyy年MM月dd日(HH:mm)}〜{End:HH:mm}の予定。";
    public TodoMetadata? TodoMetadata => FavGCalSchedulerClone.App.Services.TagService.GetTodoMetadata(this);
    public string TodoPriority => TodoMetadata?.Priority ?? "";
    public int TodoProgress => TodoMetadata?.Progress ?? 0;
    public string TodoProgressText => TodoMetadata?.ProgressText ?? "";
    public string TodoPriorityDisplayText => string.IsNullOrWhiteSpace(TodoPriority) ? "-" : TodoPriority;
    public bool IsTodoDone => TodoMetadata?.IsDone == true;
    public bool IsOverdueTodo => IsTodoLike && !IsTodoDone && Start.Date < DateTime.Today;
    public bool IsRecurringMaster => !string.IsNullOrWhiteSpace(RecurrenceJson) && !IsRecurrenceException;
    public bool IsRecurringSeriesItem => IsRecurringMaster || IsRecurrenceException || IsGeneratedOccurrence || !string.IsNullOrWhiteSpace(RecurringEventId) || !string.IsNullOrWhiteSpace(RecurringParentId);
    public string DirtyFieldsDisplayText => Services.EventDirtyFieldTracker.ToDisplayText(DirtyFields);

    private static string SingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
