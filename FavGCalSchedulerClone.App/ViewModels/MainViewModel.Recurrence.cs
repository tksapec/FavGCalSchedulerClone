using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FavGCalSchedulerClone.App.Commands;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Win32;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{

    private async Task SaveEventWithRecurrenceAsync(RecurrenceEditScope? recurrenceScope)
    {
        var candidate = BuildEditedEventAsync();
        if (candidate is null)
        {
            return;
        }

        if (SelectedEvent is null || recurrenceScope is null)
        {
            var undoSnapshot = SelectedEvent is null ? null : UndoService.Clone(SelectedEvent);
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            if (undoSnapshot is not null)
            {
                CaptureUndo("予定編集", [undoSnapshot]);
            }

            await RecordScheduleHistoryAsync(candidate);
            await RefreshCalendarAsync();
            SelectedEvent = candidate;
            Status = "予定を保存しました。";
            await SyncAfterLocalChangeAsync();
            return;
        }

        var undoSnapshots = await LoadRecurrenceUndoSnapshotsAsync(
            SelectedEvent,
            recurrenceScope.Value,
            isDelete: false,
            targetCalendarId: candidate.CalendarId);
        IReadOnlyList<string> createdEventIds = recurrenceScope.Value switch
        {
            RecurrenceEditScope.ThisOccurrence => await SaveSingleOccurrenceAsync(candidate),
            RecurrenceEditScope.ThisAndFollowing => await SaveThisAndFollowingAsync(candidate),
            RecurrenceEditScope.AllEvents => await SaveEntireSeriesAsync(candidate),
            _ => []
        };
        CaptureUndo("繰り返し予定編集", undoSnapshots, createdEventIds);

        await RefreshCalendarAsync();
        await RecordScheduleHistoryAsync(candidate);
        Status = "予定を保存しました。";
        await SyncAfterLocalChangeAsync();
    }

    private async Task DeleteEventWithRecurrenceAsync(RecurrenceEditScope? recurrenceScope)
    {
        if (SelectedEvent is null)
        {
            return;
        }

        if (recurrenceScope is null)
        {
            var undoSnapshot = UndoService.Clone(SelectedEvent);
            var deleted = UndoService.Clone(SelectedEvent);
            deleted.IsDeleted = true;
            deleted.IsDirty = true;
            await CalendarRepositoryAtomicWriter.SaveEventsAsync(_repository, [deleted]);
            CaptureUndo("予定削除", [undoSnapshot]);
            SelectedEvent = null;
            await RefreshCalendarAsync();
            Status = "予定を削除しました。";
            await SyncAfterLocalChangeAsync();
            return;
        }

        var selected = SelectedEvent;
        var undoSnapshots = await LoadRecurrenceUndoSnapshotsAsync(selected, recurrenceScope.Value, isDelete: true);
        IReadOnlyList<string> createdEventIds = recurrenceScope.Value switch
        {
            RecurrenceEditScope.ThisOccurrence => await DeleteSingleOccurrenceAsync(),
            RecurrenceEditScope.ThisAndFollowing => await DeleteThisAndFollowingAsync(),
            RecurrenceEditScope.AllEvents => await DeleteEntireSeriesAsync(),
            _ => []
        };
        CaptureUndo("繰り返し予定削除", undoSnapshots, createdEventIds);

        SelectedEvent = null;
        await RefreshCalendarAsync();
        Status = "予定を削除しました。";
        await SyncAfterLocalChangeAsync();
    }

    private async Task<IReadOnlyList<string>> SaveSingleOccurrenceAsync(CalendarEvent candidate)
    {
        if (SelectedEvent is null)
        {
            return [];
        }

        if (!SelectedEvent.IsGeneratedOccurrence && SelectedEvent.IsRecurrenceException)
        {
            candidate.IsRecurrenceException = true;
            candidate.RecurringParentId = SelectedEvent.RecurringParentId;
            candidate.RecurringEventId = SelectedEvent.RecurringEventId;
            candidate.OriginalStart = SelectedEvent.OriginalStart;
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            SelectedEvent = candidate;
            return [];
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            SelectedEvent = candidate;
            return [];
        }

        var created = SelectedEvent.IsGeneratedOccurrence;
        candidate.Id = created ? Guid.NewGuid().ToString("N") : candidate.Id;
        candidate.GoogleEventId = SelectedEvent.GoogleEventId;
        candidate.RecurringParentId = master.Id;
        candidate.RecurringEventId = master.GoogleEventId;
        candidate.OriginalStart = SelectedEvent.OriginalStart ?? SelectedEvent.Start;
        candidate.IsRecurrenceException = true;
        candidate.RecurrenceJson = null;
        await _repository.SaveEventAsync(candidate);
        SelectedEvent = candidate;
        return created ? [candidate.Id] : [];
    }

    private async Task<IReadOnlyList<string>> SaveEntireSeriesAsync(CalendarEvent candidate)
    {
        if (SelectedEvent is null)
        {
            await _repository.SaveEventAsync(candidate);
            SelectedEvent = candidate;
            return [];
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            SelectedEvent = candidate;
            return [];
        }

        var target = CloneEventForEditing(master);
        ApplySeriesEditValues(target, candidate, SelectedEvent);
        target.IsDirty = true;
        if (string.Equals(master.CalendarId, target.CalendarId, StringComparison.Ordinal))
        {
            await SaveEventWithCalendarMoveAsync(target, master);
            SelectedEvent = target;
            return [];
        }

        var writes = PrepareCalendarMoveWrites(target, master).ToList();
        foreach (var child in await _repository.LoadSeriesEventsAsync(master.Id, master.GoogleEventId))
        {
            if (!child.IsRecurrenceException)
            {
                continue;
            }

            var movedChild = CloneEventForEditing(child);
            movedChild.CalendarId = target.CalendarId;
            movedChild.GoogleEventId = null;
            movedChild.LastSyncedGoogleEtag = null;
            movedChild.LastSyncedAt = null;
            movedChild.RecurringParentId = target.Id;
            movedChild.RecurringEventId = null;
            movedChild.IsDirty = true;
            writes.Add(movedChild);
        }

        await CalendarRepositoryAtomicWriter.SaveEventsAsync(_repository, writes);
        SelectedEvent = target;
        return [];
    }

    private async Task<IReadOnlyList<string>> SaveThisAndFollowingAsync(CalendarEvent candidate)
    {
        if (SelectedEvent is null)
        {
            return [];
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            SelectedEvent = candidate;
            return [];
        }

        var splitStart = SelectedEvent.OriginalStart ?? SelectedEvent.Start;
        if (splitStart <= master.Start)
        {
            // Splitting at the first occurrence is equivalent to editing the whole series.
            // Keeping a one-occurrence source series would create two series at the same start.
            return await SaveEntireSeriesAsync(candidate);
        }

        var writes = new List<CalendarEvent>();
        var createdIds = new List<string>();
        var original = CloneEventForEditing(master);
        original.RecurrenceJson = RecurrenceRuleHelper.BuildSplitSourceRecurrenceJson(master, splitStart);
        original.IsDirty = true;
        writes.Add(original);

        var future = CloneEventForEditing(master);
        future.Id = Guid.NewGuid().ToString("N");
        future.GoogleEventId = null;
        future.LastSyncedGoogleEtag = null;
        future.RecurringEventId = null;
        future.RecurringParentId = null;
        future.OriginalStart = null;
        future.IsRecurrenceException = false;
        // The new series must be anchored at the occurrence where the split happens,
        // not at the original master's DTSTART. ApplySeriesEditValues then applies any
        // date/time change made in the editor relative to this occurrence.
        future.Start = SelectedEvent.Start;
        future.End = SelectedEvent.End;
        future.StartTimeZoneId = SelectedEvent.StartTimeZoneId ?? master.StartTimeZoneId;
        future.EndTimeZoneId = SelectedEvent.EndTimeZoneId ?? master.EndTimeZoneId ?? future.StartTimeZoneId;
        ApplySeriesEditValues(future, candidate, SelectedEvent);
        future.RecurrenceJson = RecurrenceRuleHelper.BuildSplitFutureRecurrenceJson(master, splitStart);
        future.IsDirty = true;
        writes.Add(future);
        createdIds.Add(future.Id);

        foreach (var child in await _repository.LoadSeriesEventsAsync(master.Id, master.GoogleEventId))
        {
            if (!child.IsRecurrenceException || child.OriginalStart is null || child.OriginalStart < splitStart)
            {
                continue;
            }

            var moved = CloneEventForEditing(child);
            moved.Id = Guid.NewGuid().ToString("N");
            moved.CalendarId = future.CalendarId;
            moved.GoogleEventId = null;
            moved.LastSyncedGoogleEtag = null;
            moved.LastSyncedAt = null;
            moved.RecurringParentId = future.Id;
            moved.RecurringEventId = null;
            moved.IsDirty = true;
            writes.Add(moved);
            createdIds.Add(moved.Id);
        }

        await CalendarRepositoryAtomicWriter.SaveEventsAsync(_repository, writes);
        SelectedEvent = future;
        return createdIds;
    }

    private async Task<IReadOnlyList<string>> DeleteSingleOccurrenceAsync()
    {
        if (SelectedEvent is null)
        {
            return [];
        }

        if (SelectedEvent.IsRecurrenceException && !SelectedEvent.IsGeneratedOccurrence)
        {
            var deleted = CloneEventForEditing(SelectedEvent);
            deleted.IsDeleted = true;
            deleted.IsDirty = true;
            await CalendarRepositoryAtomicWriter.SaveEventsAsync(_repository, [deleted]);
            return [];
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            var deleted = CloneEventForEditing(SelectedEvent);
            deleted.IsDeleted = true;
            deleted.IsDirty = true;
            await CalendarRepositoryAtomicWriter.SaveEventsAsync(_repository, [deleted]);
            return [];
        }

        var tombstone = new CalendarEvent
        {
            Title = SelectedEvent.Title,
            Description = SelectedEvent.Description,
            Location = SelectedEvent.Location,
            CalendarId = SelectedEvent.CalendarId,
            Start = SelectedEvent.Start,
            End = SelectedEvent.End,
            IsAllDay = SelectedEvent.IsAllDay,
            ColorId = SelectedEvent.ColorId,
            IsDeleted = true,
            IsDirty = true,
            IsTodoLike = SelectedEvent.IsTodoLike,
            RecurringParentId = master.Id,
            RecurringEventId = master.GoogleEventId,
            OriginalStart = SelectedEvent.OriginalStart ?? SelectedEvent.Start,
            IsRecurrenceException = true
        };
        var masterWrite = CloneEventForEditing(master);
        masterWrite.RecurrenceJson = RecurrenceRuleHelper.AddExDate(master.RecurrenceJson, tombstone.OriginalStart.Value, master.IsAllDay);
        masterWrite.IsDirty = true;
        await CalendarRepositoryAtomicWriter.SaveEventsAsync(_repository, [masterWrite, tombstone]);
        return [tombstone.Id];
    }

    private async Task<IReadOnlyList<string>> DeleteEntireSeriesAsync()
    {
        if (SelectedEvent is null)
        {
            return [];
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        var writes = new List<CalendarEvent>();
        if (master is not null)
        {
            var deletedMaster = CloneEventForEditing(master);
            deletedMaster.IsDeleted = true;
            deletedMaster.IsDirty = true;
            writes.Add(deletedMaster);
        }

        foreach (var child in await _repository.LoadSeriesEventsAsync(master?.Id ?? SelectedEvent.RecurringParentId, master?.GoogleEventId ?? SelectedEvent.RecurringEventId))
        {
            var deletedChild = CloneEventForEditing(child);
            deletedChild.IsDeleted = true;
            deletedChild.IsDirty = true;
            writes.Add(deletedChild);
        }

        if (master is null && writes.Count == 0)
        {
            var deleted = CloneEventForEditing(SelectedEvent);
            deleted.IsDeleted = true;
            deleted.IsDirty = true;
            writes.Add(deleted);
        }

        await CalendarRepositoryAtomicWriter.SaveEventsAsync(_repository, writes);
        return [];
    }

    private async Task<IReadOnlyList<string>> DeleteThisAndFollowingAsync()
    {
        if (SelectedEvent is null)
        {
            return [];
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            var deleted = CloneEventForEditing(SelectedEvent);
            deleted.IsDeleted = true;
            deleted.IsDirty = true;
            await CalendarRepositoryAtomicWriter.SaveEventsAsync(_repository, [deleted]);
            return [];
        }

        var splitStart = SelectedEvent.OriginalStart ?? SelectedEvent.Start;
        if (splitStart <= master.Start)
        {
            return await DeleteEntireSeriesAsync();
        }

        var writes = new List<CalendarEvent>();
        var masterWrite = CloneEventForEditing(master);
        masterWrite.RecurrenceJson = RecurrenceRuleHelper.BuildSplitSourceRecurrenceJson(master, splitStart);
        masterWrite.IsDirty = true;
        writes.Add(masterWrite);

        foreach (var child in await _repository.LoadSeriesEventsAsync(master.Id, master.GoogleEventId))
        {
            if (child.OriginalStart is not null && child.OriginalStart >= splitStart)
            {
                var deletedChild = CloneEventForEditing(child);
                deletedChild.IsDeleted = true;
                deletedChild.IsDirty = true;
                writes.Add(deletedChild);
            }
        }

        await CalendarRepositoryAtomicWriter.SaveEventsAsync(_repository, writes);
        return [];
    }

    private async Task<IReadOnlyList<CalendarEvent>> LoadRecurrenceUndoSnapshotsAsync(
        CalendarEvent selectedEvent,
        RecurrenceEditScope recurrenceScope,
        bool isDelete,
        string? targetCalendarId = null)
    {
        var master = await ResolveSeriesMasterAsync(selectedEvent);
        if (master is null)
        {
            if (selectedEvent.IsGeneratedOccurrence)
            {
                return [];
            }

            return await _repository.FindMasterByIdAsync(selectedEvent.Id) is { } persisted
                ? [UndoService.Clone(persisted)]
                : [];
        }

        if (recurrenceScope == RecurrenceEditScope.ThisOccurrence)
        {
            if (selectedEvent.IsGeneratedOccurrence)
            {
                return isDelete ? [UndoService.Clone(master)] : [];
            }

            return await _repository.FindMasterByIdAsync(selectedEvent.Id) is { } persisted
                ? [UndoService.Clone(persisted)]
                : [];
        }

        var splitStart = selectedEvent.OriginalStart ?? selectedEvent.Start;
        var affectsEntireSeries = recurrenceScope == RecurrenceEditScope.AllEvents || splitStart <= master.Start;
        var movesEntireSeriesToAnotherCalendar = !isDelete
            && affectsEntireSeries
            && !string.IsNullOrWhiteSpace(targetCalendarId)
            && !string.Equals(master.CalendarId, targetCalendarId, StringComparison.Ordinal);
        if (!isDelete && !movesEntireSeriesToAnotherCalendar)
        {
            return [UndoService.Clone(master)];
        }

        var snapshots = new List<CalendarEvent> { UndoService.Clone(master) };
        foreach (var child in await _repository.LoadSeriesEventsAsync(master.Id, master.GoogleEventId))
        {
            if (movesEntireSeriesToAnotherCalendar
                || affectsEntireSeries
                || child.OriginalStart is not null && child.OriginalStart >= splitStart)
            {
                snapshots.Add(UndoService.Clone(child));
            }
        }

        return snapshots;
    }

    private async Task<CalendarEvent?> ResolveSeriesMasterAsync(CalendarEvent selectedEvent)
    {
        if (selectedEvent.IsRecurringMaster)
        {
            return await _repository.FindMasterByIdAsync(selectedEvent.Id);
        }

        if (!string.IsNullOrWhiteSpace(selectedEvent.RecurringParentId))
        {
            return _storedEvents.FirstOrDefault(item => item.Id == selectedEvent.RecurringParentId && item.IsRecurringMaster)
                ?? await _repository.FindMasterByIdAsync(selectedEvent.RecurringParentId);
        }

        if (!string.IsNullOrWhiteSpace(selectedEvent.RecurringEventId))
        {
            return _storedEvents.FirstOrDefault(item => item.GoogleEventId == selectedEvent.RecurringEventId && item.IsRecurringMaster)
                ?? (await _repository.LoadSeriesEventsAsync(null, selectedEvent.RecurringEventId)).FirstOrDefault(item => item.IsRecurringMaster);
        }

        return null;
    }

    private void ApplySeriesEditValues(CalendarEvent target, CalendarEvent candidate, CalendarEvent selectedEvent)
    {
        var dayShift = (candidate.Start.Date - selectedEvent.Start.Date).Days;
        target.Title = candidate.Title;
        target.Description = candidate.Description;
        target.Location = candidate.Location;
        target.CalendarId = candidate.CalendarId;
        target.IsAllDay = candidate.IsAllDay;
        target.ColorId = candidate.ColorId;
        target.ReminderMinutesBeforeStart = candidate.ReminderMinutesBeforeStart;
        target.AppReminderMinutesBeforeStart = [.. candidate.AppReminderMinutesBeforeStart];
        target.GoogleEmailReminderMinutesBeforeStart = [.. candidate.GoogleEmailReminderMinutesBeforeStart];
        target.AppReminderEnabled = candidate.AppReminderEnabled;
        target.GoogleEmailReminderEnabled = candidate.GoogleEmailReminderEnabled;
        target.GoogleReminderMetadata = candidate.GoogleReminderMetadata?.Clone();
        target.StartTimeZoneId = candidate.IsAllDay ? null : candidate.StartTimeZoneId;
        target.EndTimeZoneId = candidate.IsAllDay ? null : candidate.EndTimeZoneId ?? candidate.StartTimeZoneId;

        var targetStartDate = target.Start.Date.AddDays(dayShift);
        if (candidate.IsAllDay)
        {
            var durationDays = Math.Max(1, (candidate.End.Date - candidate.Start.Date).Days);
            target.Start = new DateTimeOffset(targetStartDate);
            target.End = new DateTimeOffset(targetStartDate.AddDays(durationDays));
            return;
        }

        var endDayOffset = (candidate.End.Date - candidate.Start.Date).Days;
        var startWallClock = targetStartDate.Add(candidate.Start.TimeOfDay);
        var endWallClock = targetStartDate.AddDays(endDayOffset).Add(candidate.End.TimeOfDay);
        target.Start = CreateSeriesDateTimeOffset(
            startWallClock,
            target.StartTimeZoneId,
            target.Start.Offset);
        target.End = CreateSeriesDateTimeOffset(
            endWallClock,
            target.EndTimeZoneId ?? target.StartTimeZoneId,
            target.End.Offset);
    }

    private static DateTimeOffset CreateSeriesDateTimeOffset(
        DateTime wallClock,
        string? timeZoneId,
        TimeSpan preferredOffset)
    {
        if (GoogleCalendarTimeZone.TryCreateDateTimeOffset(
                wallClock,
                timeZoneId,
                preferredOffset,
                out var value))
        {
            return value;
        }

        // BuildEditedEventAsync already validates the edited occurrence. The only
        // remaining failure case is normally an invalid wall-clock on the older series
        // anchor date (for example a DST gap). Keep the prior offset rather than
        // silently applying the machine-local zone.
        return new DateTimeOffset(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), preferredOffset);
    }
}
