using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public sealed class UndoService
{
    private UndoOperation? _lastOperation;

    public bool CanUndo => _lastOperation is not null;
    public string StatusText => _lastOperation?.Description ?? "";

    public void Capture(
        string description,
        IEnumerable<CalendarEvent?> beforeEvents,
        IEnumerable<string>? createdEventIds = null)
    {
        var snapshots = beforeEvents
            .Where(item => item is not null)
            .Select(item => Clone(item!))
            .ToArray();
        var createdIds = createdEventIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (snapshots.Length == 0 && createdIds.Length == 0)
        {
            return;
        }

        _lastOperation = new UndoOperation(description, snapshots, createdIds);
    }

    public UndoOperation? Peek() => _lastOperation;

    public bool Consume(UndoOperation operation)
    {
        if (!ReferenceEquals(_lastOperation, operation))
        {
            return false;
        }

        _lastOperation = null;
        return true;
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
            StartTimeZoneId = source.StartTimeZoneId,
            EndTimeZoneId = source.EndTimeZoneId,
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
