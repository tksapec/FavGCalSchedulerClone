using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    public async Task<int> BulkUpdateEventsAsync(IReadOnlyCollection<string> localIds, BulkEventUpdateRequest request)
    {
        if (!request.HasUpdates)
        {
            return 0;
        }

        var events = await LoadEventsForBulkOperationAsync(localIds);
        if (events.Count == 0)
        {
            return 0;
        }

        CaptureUndo("一括編集", events);
        var updated = 0;
        foreach (var calendarEvent in events)
        {
            var original = UndoService.Clone(calendarEvent);
            if (request.UpdatesCalendar && !string.IsNullOrWhiteSpace(request.CalendarId))
            {
                calendarEvent.CalendarId = request.CalendarId;
            }

            if (request.UpdatesColor)
            {
                calendarEvent.ColorId = request.ColorId;
            }

            if (request.UpdatesReminder)
            {
                var minutes = request.ReminderMinutesBeforeStart ?? calendarEvent.ReminderMinutesBeforeStart;
                var appEnabled = request.AppReminderEnabled ?? calendarEvent.IsAppReminderEnabled;
                var emailEnabled = request.GoogleEmailReminderEnabled ?? calendarEvent.IsGoogleEmailReminderEnabled;
                if (minutes is null || (!appEnabled && !emailEnabled))
                {
                    calendarEvent.ReminderMinutesBeforeStart = null;
                    calendarEvent.AppReminderMinutesBeforeStart = [];
                    calendarEvent.GoogleEmailReminderMinutesBeforeStart = [];
                    calendarEvent.IsAppReminderEnabled = false;
                    calendarEvent.IsGoogleEmailReminderEnabled = false;
                    calendarEvent.GoogleReminderMetadata = CreateCommonGoogleReminderMetadata(calendarEvent.GoogleReminderMetadata, [], []);
                }
                else
                {
                    var appReminderMinutes = appEnabled ? CalendarEvent.NormalizeReminderMinutes([minutes.Value]) : [];
                    var googleEmailReminderMinutes = emailEnabled ? CalendarEvent.NormalizeReminderMinutes([minutes.Value]) : [];
                    calendarEvent.ReminderMinutesBeforeStart = appReminderMinutes.Count == 0 ? null : appReminderMinutes[0];
                    calendarEvent.AppReminderMinutesBeforeStart = appReminderMinutes.ToList();
                    calendarEvent.GoogleEmailReminderMinutesBeforeStart = googleEmailReminderMinutes.ToList();
                    calendarEvent.IsAppReminderEnabled = appEnabled;
                    calendarEvent.IsGoogleEmailReminderEnabled = emailEnabled;
                    calendarEvent.GoogleReminderMetadata = CreateCommonGoogleReminderMetadata(calendarEvent.GoogleReminderMetadata, appReminderMinutes, googleEmailReminderMinutes);
                }
            }

            calendarEvent.IsDirty = true;
            await SaveEventWithCalendarMoveAsync(calendarEvent, original);
            updated++;
        }

        await RefreshCalendarAsync();
        Status = $"一括編集しました: {updated} 件";
        await SyncAfterLocalChangeAsync();
        return updated;
    }

    public async Task<int> BulkDeleteEventsAsync(IReadOnlyCollection<string> localIds)
    {
        var events = await LoadEventsForBulkOperationAsync(localIds);
        if (events.Count == 0)
        {
            return 0;
        }

        CaptureUndo("一括削除", events);
        var deleted = 0;
        foreach (var calendarEvent in events)
        {
            await _repository.DeleteEventAsync(calendarEvent);
            deleted++;
        }

        SelectedEvent = null;
        await RefreshCalendarAsync();
        Status = $"一括削除しました: {deleted} 件";
        await SyncAfterLocalChangeAsync();
        return deleted;
    }

    public async Task<bool> UndoLastChangeAsync()
    {
        var operation = _undoService.Pop();
        NotifyUndoStateChanged();
        if (operation is null)
        {
            return false;
        }

        foreach (var snapshot in operation.BeforeEvents)
        {
            var current = await _repository.FindMasterByIdAsync(snapshot.Id);
            await CreateDestinationDeleteForSyncedMoveAsync(current, snapshot);

            var restored = UndoService.Clone(snapshot);
            restored.IsDirty = true;
            restored.LastSyncedAt = null;
            await _repository.SaveEventAsync(restored);
            await RemoveUndoMoveTombstonesAsync(restored);
        }

        SelectedEvent = operation.BeforeEvents.Count == 1
            ? await _repository.FindMasterByIdAsync(operation.BeforeEvents[0].Id)
            : null;
        await RefreshCalendarAsync();
        Status = $"元に戻しました: {operation.Description}";
        await SyncAfterLocalChangeAsync();
        return true;
    }

    private async Task<IReadOnlyList<CalendarEvent>> LoadEventsForBulkOperationAsync(IReadOnlyCollection<string> localIds)
    {
        var ids = localIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var events = new List<CalendarEvent>();
        foreach (var id in ids)
        {
            if (await _repository.FindMasterByIdAsync(id) is { } calendarEvent)
            {
                events.Add(calendarEvent);
            }
        }

        return events;
    }

    private async Task CreateDestinationDeleteForSyncedMoveAsync(CalendarEvent? current, CalendarEvent before)
    {
        if (current is null
            || string.IsNullOrWhiteSpace(current.GoogleEventId)
            || string.Equals(current.CalendarId, before.CalendarId, StringComparison.Ordinal)
            || string.Equals(current.GoogleEventId, before.GoogleEventId, StringComparison.Ordinal))
        {
            return;
        }

        var destinationTombstone = UndoService.Clone(current);
        destinationTombstone.Id = Guid.NewGuid().ToString("N");
        destinationTombstone.IsDeleted = true;
        destinationTombstone.IsDirty = true;
        destinationTombstone.LastSyncedAt = null;
        await _repository.SaveEventAsync(destinationTombstone);
    }

    private async Task RemoveUndoMoveTombstonesAsync(CalendarEvent restored)
    {
        if (string.IsNullOrWhiteSpace(restored.GoogleEventId))
        {
            return;
        }

        var candidates = await _repository.LoadEventsAsync(
            restored.Start.AddDays(-1),
            restored.End.AddDays(1),
            includeDeleted: true);
        foreach (var tombstone in candidates.Where(item =>
                     item.Id != restored.Id
                     && item.IsDeleted
                     && string.Equals(item.CalendarId, restored.CalendarId, StringComparison.Ordinal)
                     && string.Equals(item.GoogleEventId, restored.GoogleEventId, StringComparison.Ordinal)))
        {
            await _repository.HardDeleteEventAsync(tombstone.Id);
        }
    }

    private void CaptureUndo(string description, IEnumerable<CalendarEvent?> beforeEvents)
    {
        _undoService.Capture(description, beforeEvents);
        NotifyUndoStateChanged();
    }

    private void NotifyUndoStateChanged()
    {
        OnPropertyChanged(nameof(CanUndoLastChange));
        OnPropertyChanged(nameof(UndoStatusText));
        UndoLastChangeCommand.RaiseCanExecuteChanged();
    }
}
