using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public sealed class UndoService
{
    private UndoOperation? _lastOperation;

    public bool CanUndo => _lastOperation is not null;
    public string StatusText => _lastOperation?.Description ?? "";

    public void Capture(string description, IEnumerable<CalendarEvent?> beforeEvents)
    {
        var snapshots = beforeEvents
            .Where(item => item is not null)
            .Select(item => Clone(item!))
            .ToArray();
        if (snapshots.Length == 0)
        {
            return;
        }

        _lastOperation = new UndoOperation(description, snapshots);
    }

    public UndoOperation? Pop()
    {
        var operation = _lastOperation;
        _lastOperation = null;
        return operation;
    }

    public void Clear()
    {
        _lastOperation = null;
    }

    public static CalendarEvent Clone(CalendarEvent source)
    {
        return new CalendarEvent
        {
            Id = source.Id,
            GoogleEventId = source.GoogleEventId,
            LastSyncedGoogleEtag = source.LastSyncedGoogleEtag,
            RecurringEventId = source.RecurringEventId,
            RecurringParentId = source.RecurringParentId,
            OriginalStart = source.OriginalStart,
            IsRecurrenceException = source.IsRecurrenceException,
            CalendarId = source.CalendarId,
            Title = source.Title,
            Description = source.Description,
            Location = source.Location,
            Start = source.Start,
            End = source.End,
            IsAllDay = source.IsAllDay,
            ColorId = source.ColorId,
            RecurrenceJson = source.RecurrenceJson,
            IsDeleted = source.IsDeleted,
            UpdatedAt = source.UpdatedAt,
            LastSyncedAt = source.LastSyncedAt,
            IsDirty = source.IsDirty,
            DirtyFields = source.DirtyFields,
            IsTodoLike = source.IsTodoLike,
            ReminderMinutesBeforeStart = source.ReminderMinutesBeforeStart,
            AppReminderMinutesBeforeStart = [.. source.AppReminderMinutesBeforeStart],
            GoogleEmailReminderMinutesBeforeStart = [.. source.GoogleEmailReminderMinutesBeforeStart],
            AppReminderEnabled = source.AppReminderEnabled,
            GoogleEmailReminderEnabled = source.GoogleEmailReminderEnabled,
            GoogleReminderMetadata = source.GoogleReminderMetadata?.Clone(),
            DisplayColor = source.DisplayColor,
            DisplayForegroundColor = source.DisplayForegroundColor,
            ToolTipText = source.ToolTipText,
            IsGeneratedOccurrence = source.IsGeneratedOccurrence
        };
    }
}
