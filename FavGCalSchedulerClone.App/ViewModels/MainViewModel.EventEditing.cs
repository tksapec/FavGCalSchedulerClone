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

    public void NewEvent()
    {
        BeginNewEvent(SelectedDay?.Date ?? DateTime.Today);
    }

    public void CopySelectedEventLabel()
    {
        if (SelectedEvent is null)
        {
            return;
        }

        _labelClipboard = new LabelClipboardItem(CloneEventForEditing(SelectedEvent), Cut: false);
        OnPropertyChanged(nameof(CanPasteEventLabel));
    }

    public void CutSelectedEventLabel()
    {
        if (!CanCutSelectedEventLabel || SelectedEvent is null)
        {
            return;
        }

        _labelClipboard = new LabelClipboardItem(CloneEventForEditing(SelectedEvent), Cut: true);
        OnPropertyChanged(nameof(CanPasteEventLabel));
    }

    public async Task<bool> PasteEventLabelAsync(DateTime targetDate)
    {
        if (_labelClipboard is not { } clipboard)
        {
            return false;
        }

        var pasted = CloneEventAsNewLocalEvent(clipboard.Event);
        var dayShift = (targetDate.Date - clipboard.Event.Start.Date).Days;
        pasted.Start = pasted.Start.AddDays(dayShift);
        pasted.End = pasted.End.AddDays(dayShift);
        await _repository.SaveEventAsync(pasted);

        if (clipboard.Cut)
        {
            var source = clipboard.Event;
            source.IsDeleted = true;
            source.IsDirty = true;
            await _repository.SaveEventAsync(source);
            _labelClipboard = null;
            OnPropertyChanged(nameof(CanPasteEventLabel));
        }

        _pendingSelectedDate = targetDate.Date;
        SelectedEvent = pasted;
        await RefreshCalendarAsync();
        await SyncAfterLocalChangeAsync();
        return true;
    }

    public void BeginNewEvent(DateTime date)
    {
        SelectedEvent = null;
        Title = _settings.ReuseLastScheduleInput ? _scheduleTitleHistory.FirstOrDefault() ?? "" : "";
        Description = "";
        Location = _settings.ReuseLastScheduleInput ? _scheduleLocationHistory.FirstOrDefault() ?? "" : "";
        StartDate = date.Date;
        EndDate = date.Date;
        StartTime = "09:00";
        EndTime = "10:00";
        IsAllDay = _settings.DefaultNewEventIsAllDay;
        ReminderMinutesBeforeStart = _settings.DefaultScheduleReminderMinutes;
        AppReminderMinutesBeforeStart = _settings.DefaultScheduleReminderMinutes is int defaultMinutes ? [defaultMinutes] : [];
        GoogleEmailReminderMinutesBeforeStart = [];
        IsAppReminderEnabled = _settings.DefaultScheduleReminderMinutes is not null;
        IsGoogleEmailReminderEnabled = false;
        EditorColorId = null;
        Status = "新しいスケジュールを入力してください。";
    }

    public async Task SaveCurrentEventAsync(RecurrenceEditScope? recurrenceScope = null)
    {
        await SaveEventWithRecurrenceAsync(recurrenceScope);
    }

    public async Task DeleteSelectedEventAsync(RecurrenceEditScope? recurrenceScope = null)
    {
        await DeleteEventWithRecurrenceAsync(recurrenceScope);
    }

    public async Task<CalendarEvent?> FindEventByIdAsync(string localId)
    {
        return await _repository.FindEventByIdAsync(localId);
    }

    private void LoadEditor(CalendarEvent? calendarEvent)
    {
        if (calendarEvent is null)
        {
            return;
        }

        EditorCalendarId = calendarEvent.CalendarId;
        Title = calendarEvent.Title;
        Description = calendarEvent.Description ?? "";
        Location = calendarEvent.Location ?? "";
        StartDate = calendarEvent.Start.Date;
        EndDate = calendarEvent.IsAllDay ? calendarEvent.End.Date.AddDays(-1) : calendarEvent.End.Date;
        StartTime = calendarEvent.Start.ToString("HH:mm", CultureInfo.InvariantCulture);
        EndTime = calendarEvent.End.ToString("HH:mm", CultureInfo.InvariantCulture);
        IsAllDay = calendarEvent.IsAllDay;
        ReminderMinutesBeforeStart = calendarEvent.ReminderMinutesBeforeStart;
        AppReminderMinutesBeforeStart = calendarEvent.EffectiveAppReminderMinutesBeforeStart;
        GoogleEmailReminderMinutesBeforeStart = calendarEvent.EffectiveGoogleEmailReminderMinutesBeforeStart;
        IsAppReminderEnabled = calendarEvent.IsAppReminderEnabled;
        IsGoogleEmailReminderEnabled = calendarEvent.IsGoogleEmailReminderEnabled;
        EditorColorId = calendarEvent.ColorId;
    }

    private async Task SaveEventWithCalendarMoveAsync(CalendarEvent candidate, CalendarEvent? original)
    {
        if (original is not null
            && !string.IsNullOrWhiteSpace(original.GoogleEventId)
            && !string.Equals(original.CalendarId, candidate.CalendarId, StringComparison.Ordinal))
        {
            var tombstone = CloneEventForEditing(original);
            tombstone.Id = Guid.NewGuid().ToString("N");
            tombstone.IsDeleted = true;
            tombstone.IsDirty = true;
            tombstone.LastSyncedAt = null;
            await _repository.SaveEventAsync(tombstone);

            candidate.GoogleEventId = null;
            candidate.LastSyncedGoogleEtag = null;
            candidate.LastSyncedAt = null;
            if (candidate.IsRecurrenceException)
            {
                candidate.RecurringEventId = null;
                candidate.RecurringParentId = null;
                candidate.OriginalStart = null;
            }
        }

        await _repository.SaveEventAsync(candidate);
    }

    private CalendarEvent? BuildEditedEventAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Status = "件名を入力してください。";
            return null;
        }

        var calendarEvent = SelectedEvent is null
            ? new CalendarEvent()
            : CloneEventForEditing(SelectedEvent);
        calendarEvent.Title = Title.Trim();
        calendarEvent.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        calendarEvent.Location = string.IsNullOrWhiteSpace(Location) ? null : Location.Trim();
        calendarEvent.CalendarId = ResolveEditorCalendarId();
        calendarEvent.IsAllDay = IsAllDay;
        var appReminderMinutes = IsAppReminderEnabled
            ? CalendarEvent.NormalizeReminderMinutes(AppReminderMinutesBeforeStart.Count > 0 ? AppReminderMinutesBeforeStart : ReminderMinutesBeforeStart is int appMinutes ? [appMinutes] : [])
            : [];
        var googleEmailReminderMinutes = IsGoogleEmailReminderEnabled
            ? CalendarEvent.NormalizeReminderMinutes(GoogleEmailReminderMinutesBeforeStart.Count > 0 ? GoogleEmailReminderMinutesBeforeStart : ReminderMinutesBeforeStart is int emailMinutes ? [emailMinutes] : [])
            : [];
        var reminderMinutes = appReminderMinutes.Count == 0 ? null : (int?)appReminderMinutes[0];
        calendarEvent.ReminderMinutesBeforeStart = reminderMinutes;
        calendarEvent.AppReminderMinutesBeforeStart = appReminderMinutes.ToList();
        calendarEvent.GoogleEmailReminderMinutesBeforeStart = googleEmailReminderMinutes.ToList();
        calendarEvent.IsAppReminderEnabled = appReminderMinutes.Count > 0;
        calendarEvent.IsGoogleEmailReminderEnabled = googleEmailReminderMinutes.Count > 0;
        calendarEvent.GoogleReminderMetadata = CreateCommonGoogleReminderMetadata(calendarEvent.GoogleReminderMetadata, appReminderMinutes, googleEmailReminderMinutes);
        calendarEvent.ColorId = EditorColorId;
        calendarEvent.IsDirty = true;
        calendarEvent.IsDeleted = false;

        if (IsAllDay)
        {
            if (EndDate.Date < StartDate.Date)
            {
                Status = "終了日は開始日以降にしてください。";
                return null;
            }

            if (SelectedEvent is { IsAllDay: true } originalAllDay
                && StartDate.Date == originalAllDay.Start.Date
                && EndDate.Date == originalAllDay.End.Date.AddDays(-1))
            {
                calendarEvent.Start = originalAllDay.Start;
                calendarEvent.End = originalAllDay.End;
            }
            else
            {
                calendarEvent.Start = new DateTimeOffset(StartDate.Date);
                calendarEvent.End = new DateTimeOffset(EndDate.Date.AddDays(1));
            }
        }
        else
        {
            if (!TryParseEditorTime(StartTime, out var startTime) || !TryParseEditorTime(EndTime, out var endTime))
            {
                Status = "時刻は HH:mm 形式、または4桁数字(例: 0900, 1234)で入力してください。";
                return null;
            }

            if (SelectedEvent is { IsAllDay: false } originalTimed
                && StartDate.Date == originalTimed.Start.Date
                && EndDate.Date == originalTimed.End.Date
                && startTime == originalTimed.Start.TimeOfDay
                && endTime == originalTimed.End.TimeOfDay)
            {
                // Editor fields contain wall-clock values only. Preserve the original offsets
                // when those fields were not edited so a UTC CI host cannot shift the instant.
                calendarEvent.Start = originalTimed.Start;
                calendarEvent.End = originalTimed.End;
            }
            else
            {
                calendarEvent.Start = new DateTimeOffset(StartDate.Date.Add(startTime));
                calendarEvent.End = new DateTimeOffset(EndDate.Date.Add(endTime));
            }
            if (calendarEvent.End <= calendarEvent.Start)
            {
                Status = "終了日時は開始日時より後にしてください。";
                return null;
            }
        }

        return calendarEvent;
    }

    internal static CalendarEvent CloneEventForEditing(CalendarEvent source)
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
            ReminderMinutesBeforeStart = source.ReminderMinutesBeforeStart,
            AppReminderEnabled = source.AppReminderEnabled,
            GoogleEmailReminderEnabled = source.GoogleEmailReminderEnabled,
            GoogleReminderMetadata = source.GoogleReminderMetadata?.Clone(),
            RecurrenceJson = source.RecurrenceJson,
            IsDeleted = source.IsDeleted,
            UpdatedAt = source.UpdatedAt,
            LastSyncedAt = source.LastSyncedAt,
            IsDirty = source.IsDirty,
            DirtyFields = source.DirtyFields,
            IsTodoLike = source.IsTodoLike,
            DisplayColor = source.DisplayColor,
            DisplayForegroundColor = source.DisplayForegroundColor,
            AppReminderMinutesBeforeStart = [.. source.AppReminderMinutesBeforeStart],
            GoogleEmailReminderMinutesBeforeStart = [.. source.GoogleEmailReminderMinutesBeforeStart],
            IsGeneratedOccurrence = source.IsGeneratedOccurrence
        };
    }

    private static GoogleReminderMetadata? CreateCommonGoogleReminderMetadata(
        GoogleReminderMetadata? existing,
        IReadOnlyList<int> appReminderMinutes,
        IReadOnlyList<int> googleEmailReminderMinutes)
    {
        if (existing is null)
        {
            return null;
        }

        var metadata = new GoogleReminderMetadata
        {
            UseDefault = false,
            Source = "explicit",
            AdoptedReminderMinutes = appReminderMinutes.Count == 0 ? null : appReminderMinutes[0],
            AdoptedReminderMethod = appReminderMinutes.Count == 0 ? null : "popup"
        };
        foreach (var minutes in appReminderMinutes)
        {
            metadata.PopupMinutes.Add(minutes);
        }

        foreach (var minutes in googleEmailReminderMinutes)
        {
            metadata.EmailMinutes.Add(minutes);
        }

        return metadata;
    }

    private static CalendarEvent CloneEventAsNewLocalEvent(CalendarEvent source)
    {
        var clone = CloneEventForEditing(source);
        clone.Id = Guid.NewGuid().ToString("N");
        clone.GoogleEventId = null;
        clone.LastSyncedGoogleEtag = null;
        clone.RecurringEventId = null;
        clone.RecurringParentId = null;
        clone.OriginalStart = null;
        clone.IsRecurrenceException = false;
        clone.RecurrenceJson = null;
        clone.IsDeleted = false;
        clone.IsDirty = true;
        clone.LastSyncedAt = null;
        clone.IsGeneratedOccurrence = false;
        return clone;
    }

    public static bool TryParseEditorTime(string? value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 4 && trimmed.All(char.IsDigit))
        {
            var hour = int.Parse(trimmed[..2], CultureInfo.InvariantCulture);
            var minute = int.Parse(trimmed[2..], CultureInfo.InvariantCulture);
            if (hour is >= 0 and <= 23 && minute is >= 0 and <= 59)
            {
                time = new TimeSpan(hour, minute, 0);
                return true;
            }

            return false;
        }

        return TimeSpan.TryParseExact(
            trimmed,
            ["h\\:mm", "hh\\:mm"],
            CultureInfo.InvariantCulture,
            out time)
            && time.Days == 0
            && time.Hours is >= 0 and <= 23
            && time.Minutes is >= 0 and <= 59
            && time.Seconds == 0;
    }

    private string ResolveEditorCalendarId()
    {
        if (AvailableCalendars.Any(item => item.Id == EditorCalendarId))
        {
            return EditorCalendarId;
        }

        if (AvailableCalendars.Any(item => item.IsSelected))
        {
            return AvailableCalendars.First(item => item.IsSelected).Id;
        }

        if (AvailableCalendars.Count > 0)
        {
            return AvailableCalendars[0].Id;
        }

        return string.IsNullOrWhiteSpace(_settings.ActiveCalendarId) ? GoogleCalendarDefaults.PrimaryCalendarId : _settings.ActiveCalendarId;
    }
}
