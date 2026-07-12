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
            if (SelectedEvent is not null)
            {
                CaptureUndo("予定編集", [SelectedEvent]);
            }

            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            await RecordScheduleHistoryAsync(candidate);
            SelectedEvent = candidate;
            await RefreshCalendarAsync();
            Status = "予定を保存しました。";
            await SyncAfterLocalChangeAsync();
            return;
        }

        CaptureUndo("繰り返し予定編集", [SelectedEvent]);

        switch (recurrenceScope.Value)
        {
            case RecurrenceEditScope.ThisOccurrence:
                await SaveSingleOccurrenceAsync(candidate);
                break;
            case RecurrenceEditScope.ThisAndFollowing:
                await SaveThisAndFollowingAsync(candidate);
                break;
            case RecurrenceEditScope.AllEvents:
                await SaveEntireSeriesAsync(candidate);
                break;
        }

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
            CaptureUndo("予定削除", [SelectedEvent]);
            await _repository.DeleteEventAsync(SelectedEvent);
            SelectedEvent = null;
            await RefreshCalendarAsync();
            Status = "予定を削除しました。";
            await SyncAfterLocalChangeAsync();
            return;
        }

        CaptureUndo("繰り返し予定削除", [SelectedEvent]);

        switch (recurrenceScope.Value)
        {
            case RecurrenceEditScope.ThisOccurrence:
                await DeleteSingleOccurrenceAsync();
                break;
            case RecurrenceEditScope.ThisAndFollowing:
                await DeleteThisAndFollowingAsync();
                break;
            case RecurrenceEditScope.AllEvents:
                await DeleteEntireSeriesAsync();
                break;
        }

        SelectedEvent = null;
        await RefreshCalendarAsync();
        Status = "予定を削除しました。";
        await SyncAfterLocalChangeAsync();
    }

    private async Task SaveSingleOccurrenceAsync(CalendarEvent candidate)
    {
        if (SelectedEvent is null)
        {
            return;
        }

        if (!SelectedEvent.IsGeneratedOccurrence && SelectedEvent.IsRecurrenceException)
        {
            candidate.IsRecurrenceException = true;
            candidate.RecurringParentId = SelectedEvent.RecurringParentId;
            candidate.RecurringEventId = SelectedEvent.RecurringEventId;
            candidate.OriginalStart = SelectedEvent.OriginalStart;
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            SelectedEvent = candidate;
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            SelectedEvent = candidate;
            return;
        }

        candidate.Id = SelectedEvent.IsGeneratedOccurrence ? Guid.NewGuid().ToString("N") : candidate.Id;
        candidate.GoogleEventId = SelectedEvent.GoogleEventId;
        candidate.RecurringParentId = master.Id;
        candidate.RecurringEventId = master.GoogleEventId;
        candidate.OriginalStart = SelectedEvent.OriginalStart ?? SelectedEvent.Start;
        candidate.IsRecurrenceException = true;
        candidate.RecurrenceJson = null;
        master.RecurrenceJson = RecurrenceRuleHelper.AddExDate(master.RecurrenceJson, candidate.OriginalStart.Value, master.IsAllDay);
        master.IsDirty = true;
        await _repository.SaveEventAsync(master);
        await _repository.SaveEventAsync(candidate);
        SelectedEvent = candidate;
    }

    private async Task SaveEntireSeriesAsync(CalendarEvent candidate)
    {
        if (SelectedEvent is null)
        {
            await _repository.SaveEventAsync(candidate);
            SelectedEvent = candidate;
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            SelectedEvent = candidate;
            return;
        }

        var target = CloneEventForEditing(master);
        ApplySeriesEditValues(target, candidate, SelectedEvent);
        target.IsDirty = true;
        await SaveEventWithCalendarMoveAsync(target, master);
        SelectedEvent = target;
    }

    private async Task SaveThisAndFollowingAsync(CalendarEvent candidate)
    {
        if (SelectedEvent is null)
        {
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await SaveEventWithCalendarMoveAsync(candidate, SelectedEvent);
            SelectedEvent = candidate;
            return;
        }

        var splitStart = SelectedEvent.OriginalStart ?? SelectedEvent.Start;
        var original = CloneEventForEditing(master);
        original.RecurrenceJson = RecurrenceRuleHelper.BuildSplitSourceRecurrenceJson(master, splitStart);
        original.IsDirty = true;
        await _repository.SaveEventAsync(original);

        var future = CloneEventForEditing(master);
        future.Id = Guid.NewGuid().ToString("N");
        future.GoogleEventId = null;
        future.LastSyncedGoogleEtag = null;
        future.RecurringEventId = null;
        future.RecurringParentId = null;
        future.OriginalStart = null;
        future.IsRecurrenceException = false;
        ApplySeriesEditValues(future, candidate, SelectedEvent);
        future.RecurrenceJson = RecurrenceRuleHelper.BuildSplitFutureRecurrenceJson(master, splitStart);
        future.IsDirty = true;
        await _repository.SaveEventAsync(future);

        foreach (var child in await _repository.LoadSeriesEventsAsync(master.Id, master.GoogleEventId))
        {
            if (!child.IsRecurrenceException || child.OriginalStart is null || child.OriginalStart < splitStart)
            {
                continue;
            }

            var moved = CloneEventForEditing(child);
            moved.Id = Guid.NewGuid().ToString("N");
            moved.GoogleEventId = null;
            moved.LastSyncedGoogleEtag = null;
            moved.RecurringParentId = future.Id;
            moved.RecurringEventId = future.GoogleEventId;
            moved.IsDirty = true;
            await _repository.SaveEventAsync(moved);
        }

        SelectedEvent = future;
    }

    private async Task DeleteSingleOccurrenceAsync()
    {
        if (SelectedEvent is null)
        {
            return;
        }

        if (SelectedEvent.IsRecurrenceException && !SelectedEvent.IsGeneratedOccurrence)
        {
            await _repository.DeleteEventAsync(SelectedEvent);
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await _repository.DeleteEventAsync(SelectedEvent);
            return;
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
        master.RecurrenceJson = RecurrenceRuleHelper.AddExDate(master.RecurrenceJson, tombstone.OriginalStart.Value, master.IsAllDay);
        master.IsDirty = true;
        await _repository.SaveEventAsync(master);
        await _repository.SaveEventAsync(tombstone);
    }

    private async Task DeleteEntireSeriesAsync()
    {
        if (SelectedEvent is null)
        {
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is not null)
        {
            await _repository.DeleteEventAsync(master);
        }

        foreach (var child in await _repository.LoadSeriesEventsAsync(master?.Id ?? SelectedEvent.RecurringParentId, master?.GoogleEventId ?? SelectedEvent.RecurringEventId))
        {
            await _repository.DeleteEventAsync(child);
        }
    }

    private async Task DeleteThisAndFollowingAsync()
    {
        if (SelectedEvent is null)
        {
            return;
        }

        var master = await ResolveSeriesMasterAsync(SelectedEvent);
        if (master is null)
        {
            await _repository.DeleteEventAsync(SelectedEvent);
            return;
        }

        var splitStart = SelectedEvent.OriginalStart ?? SelectedEvent.Start;
        master.RecurrenceJson = RecurrenceRuleHelper.BuildSplitSourceRecurrenceJson(master, splitStart);
        master.IsDirty = true;
        await _repository.SaveEventAsync(master);

        foreach (var child in await _repository.LoadSeriesEventsAsync(master.Id, master.GoogleEventId))
        {
            if (child.OriginalStart is not null && child.OriginalStart >= splitStart)
            {
                await _repository.DeleteEventAsync(child);
            }
        }
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
        target.Start = dayShift == 0
            ? new DateTimeOffset(target.Start.Date.Add(candidate.Start.TimeOfDay), candidate.Start.Offset)
            : target.Start.AddDays(dayShift).Date.Add(candidate.Start.TimeOfDay);
        target.End = dayShift == 0
            ? new DateTimeOffset(target.End.Date.Add(candidate.End.TimeOfDay), candidate.End.Offset)
            : target.End.AddDays(dayShift).Date.Add(candidate.End.TimeOfDay);

        if (candidate.IsAllDay)
        {
            var durationDays = Math.Max(1, (candidate.End.Date - candidate.Start.Date).Days);
            target.Start = new DateTimeOffset(target.Start.Date);
            target.End = new DateTimeOffset(target.Start.Date.AddDays(durationDays));
        }
        else
        {
            var duration = candidate.End - candidate.Start;
            target.End = target.Start.Add(duration);
        }
    }
}
