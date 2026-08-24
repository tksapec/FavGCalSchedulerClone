namespace FavGCalSchedulerClone.App.Models;

public sealed class CalendarEvent
{
    private string? _startTimeZoneId;
    private string? _endTimeZoneId;
    private GoogleReminderMetadata? _googleReminderMetadata;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? GoogleEventId { get; set; }
    public string? LastSyncedGoogleEtag { get; set; }
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
    public string? StartTimeZoneId
    {
        get => _startTimeZoneId ?? _googleReminderMetadata?.StartTimeZoneId;
        set
        {
            _startTimeZoneId = value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                _googleReminderMetadata ??= new GoogleReminderMetadata();
                _googleReminderMetadata.StartTimeZoneId = value;
            }
            else if (_googleReminderMetadata is not null)
            {
                _googleReminderMetadata.StartTimeZoneId = null;
            }
        }
    }

    public string? EndTimeZoneId
    {
        get => _endTimeZoneId ?? _googleReminderMetadata?.EndTimeZoneId;
        set
        {
            _endTimeZoneId = value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                _googleReminderMetadata ??= new GoogleReminderMetadata();
                _googleReminderMetadata.EndTimeZoneId = value;
            }
            else if (_googleReminderMetadata is not null)
            {
                _googleReminderMetadata.EndTimeZoneId = null;
            }
        }
    }

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
    public List<int> AppReminderMinutesBeforeStart { get; set; } = [];
    public List<int> GoogleEmailReminderMinutesBeforeStart { get; set; } = [];
    internal bool? AppReminderEnabled { get; set; }
    internal bool? GoogleEmailReminderEnabled { get; set; }
    public GoogleReminderMetadata? GoogleReminderMetadata
    {
        get => _googleReminderMetadata;
        set
        {
            _googleReminderMetadata = value;
            if (value is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_startTimeZoneId))
            {
                value.StartTimeZoneId = _startTimeZoneId;
            }
            else
            {
                _startTimeZoneId = value.StartTimeZoneId;
            }

            if (!string.IsNullOrWhiteSpace(_endTimeZoneId))
            {
                value.EndTimeZoneId = _endTimeZoneId;
            }
            else
            {
                _endTimeZoneId = value.EndTimeZoneId;
            }
        }
    }
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
    public string ReminderDisplayText => PrimaryAppReminderMinutesBeforeStart switch
    {
        null => "",
        0 => "時刻",
        < 60 => $"{PrimaryAppReminderMinutesBeforeStart}分前",
        _ when PrimaryAppReminderMinutesBeforeStart % 60 == 0 => $"{PrimaryAppReminderMinutesBeforeStart / 60}時間前",
        _ => $"{PrimaryAppReminderMinutesBeforeStart}分前"
    };
    public bool IsAppReminderEnabled
    {
        get => AppReminderEnabled ?? EffectiveAppReminderMinutesBeforeStart.Count > 0;
        set => AppReminderEnabled = value;
    }

    public bool IsGoogleEmailReminderEnabled
    {
        get => GoogleEmailReminderEnabled ?? EffectiveGoogleEmailReminderMinutesBeforeStart.Count > 0;
        set => GoogleEmailReminderEnabled = value;
    }

    public IReadOnlyList<int> EffectiveAppReminderMinutesBeforeStart
    {
        get
        {
            if (AppReminderEnabled == false)
            {
                return [];
            }

            var configured = NormalizeReminderMinutes(AppReminderMinutesBeforeStart);
            if (configured.Count > 0)
            {
                return configured;
            }

            return (AppReminderEnabled ?? ReminderMinutesBeforeStart is not null) && ReminderMinutesBeforeStart is int minutes
                ? [minutes]
                : [];
        }
    }

    public IReadOnlyList<int> EffectiveGoogleEmailReminderMinutesBeforeStart
    {
        get
        {
            if (GoogleEmailReminderEnabled == false)
            {
                return [];
            }

            var configured = NormalizeReminderMinutes(GoogleEmailReminderMinutesBeforeStart);
            if (configured.Count > 0)
            {
                return configured;
            }

            if (GoogleEmailReminderEnabled == true && ReminderMinutesBeforeStart is int minutes)
            {
                return [minutes];
            }

            return GoogleEmailReminderEnabled is null or true ? GetGoogleEmailReminderMinutes() : [];
        }
    }

    public int? PrimaryAppReminderMinutesBeforeStart
    {
        get
        {
            var values = EffectiveAppReminderMinutesBeforeStart;
            return values.Count == 0 ? null : values[0];
        }
    }

    public string DescriptionPreview => SingleLine(Description);
    public string SummaryDisplayText => IsAllDay
        ? $"{Start:yyyy年MM月dd日(終日)}〜{End.AddDays(-1):yyyy年MM月dd日(終日)}の予定。"
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

    private bool HasGoogleEmailReminder()
    {
        return GoogleReminderMetadata is not null
            && (GoogleReminderMetadata.EmailMinutes.Count > 0
                || GoogleReminderMetadata.DefaultEmailMinutes.Count > 0);
    }

    public static IReadOnlyList<int> NormalizeReminderMinutes(IEnumerable<int>? minutes)
    {
        return minutes?
            .Where(value => value >= 0)
            .Distinct()
            .Order()
            .ToArray() ?? [];
    }

    private IReadOnlyList<int> GetGoogleEmailReminderMinutes()
    {
        if (!HasGoogleEmailReminder())
        {
            return [];
        }

        var source = GoogleReminderMetadata!.UseDefault == true
            ? GoogleReminderMetadata.DefaultEmailMinutes
            : GoogleReminderMetadata.EmailMinutes;
        return NormalizeReminderMinutes(source);
    }
}
