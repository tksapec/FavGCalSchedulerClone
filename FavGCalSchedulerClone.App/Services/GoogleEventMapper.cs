using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using Google.Apis.Calendar.v3.Data;
using LocalEvent = FavGCalSchedulerClone.App.Models.CalendarEvent;

namespace FavGCalSchedulerClone.App.Services;

public static class GoogleEventMapper
{
    public static LocalEvent FromGoogleEvent(Event googleEvent, string calendarId)
    {
        return FromGoogleEvent(googleEvent, calendarId, defaultReminders: null);
    }

    public static LocalEvent FromGoogleEvent(
        Event googleEvent,
        string calendarId,
        IReadOnlyList<GoogleReminderOverride>? defaultReminders,
        bool adoptEmailRemindersAsLocalNotifications = false)
    {
        var isAllDay = googleEvent.Start?.Date is { Length: > 0 };
        var start = ParseEventDateTime(googleEvent.Start, isAllDay);
        var end = ParseEventDateTime(googleEvent.End, isAllDay);
        var reminderMetadata = CreateReminderMetadata(
            googleEvent.Reminders,
            defaultReminders,
            adoptEmailRemindersAsLocalNotifications);

        var appReminderMinutes = GetPopupReminderMinutes(reminderMetadata);
        var emailReminderMinutes = GetEmailReminderMinutes(reminderMetadata);
        var local = new LocalEvent
        {
            Id = string.IsNullOrWhiteSpace(googleEvent.Id) ? Guid.NewGuid().ToString("N") : $"g:{calendarId}:{googleEvent.Id}",
            GoogleEventId = googleEvent.Id,
            LastSyncedGoogleEtag = googleEvent.ETag,
            RecurringEventId = googleEvent.RecurringEventId,
            RecurringParentId = null,
            OriginalStart = string.IsNullOrWhiteSpace(googleEvent.RecurringEventId)
                ? null
                : ParseEventDateTime(googleEvent.OriginalStartTime, isAllDay),
            IsRecurrenceException = !string.IsNullOrWhiteSpace(googleEvent.RecurringEventId),
            CalendarId = calendarId,
            Title = googleEvent.Summary ?? "(no title)",
            Description = googleEvent.Description,
            Location = googleEvent.Location,
            Start = start,
            End = end <= start ? start.AddHours(1) : end,
            StartTimeZoneId = isAllDay ? null : googleEvent.Start?.TimeZone,
            EndTimeZoneId = isAllDay ? null : googleEvent.End?.TimeZone ?? googleEvent.Start?.TimeZone,
            IsAllDay = isAllDay,
            ColorId = googleEvent.ColorId,
            RecurrenceJson = googleEvent.Recurrence is null ? null : JsonSerializer.Serialize(googleEvent.Recurrence),
            IsDeleted = string.Equals(googleEvent.Status, "cancelled", StringComparison.OrdinalIgnoreCase),
            UpdatedAt = googleEvent.UpdatedDateTimeOffset ?? DateTimeOffset.Now,
            IsDirty = false,
            ReminderMinutesBeforeStart = FirstOrDefaultOrNull(appReminderMinutes),
            AppReminderMinutesBeforeStart = appReminderMinutes.ToList(),
            GoogleEmailReminderMinutesBeforeStart = emailReminderMinutes.ToList(),
            IsAppReminderEnabled = appReminderMinutes.Count > 0,
            IsGoogleEmailReminderEnabled = emailReminderMinutes.Count > 0,
            GoogleReminderMetadata = reminderMetadata
        };
        local.IsTodoLike = TagService.IsTodoLike(local);
        if (local.IsTodoLike)
        {
            // Preserve the fact that Google has reminders for sync cleanup, while never
            // adopting those values as application notification settings.
            local.ReminderMinutesBeforeStart = null;
            local.AppReminderMinutesBeforeStart = [];
            local.GoogleEmailReminderMinutesBeforeStart = [];
            local.IsAppReminderEnabled = false;
            local.IsGoogleEmailReminderEnabled = false;
        }
        return local;
    }

    public static Event ToGoogleEvent(LocalEvent localEvent)
    {
        var googleEvent = new Event
        {
            Summary = localEvent.Title,
            Description = localEvent.Description,
            Location = localEvent.Location,
            ColorId = localEvent.ColorId,
            Start = ToEventDateTime(localEvent.Start, localEvent.IsAllDay, localEvent.StartTimeZoneId),
            End = ToEventDateTime(localEvent.End, localEvent.IsAllDay, localEvent.EndTimeZoneId ?? localEvent.StartTimeZoneId),
            Status = localEvent.IsDeleted ? "cancelled" : "confirmed",
            Reminders = ToGoogleReminders(localEvent)
        };

        if (!string.IsNullOrWhiteSpace(localEvent.RecurrenceJson))
        {
            googleEvent.Recurrence = JsonSerializer.Deserialize<IList<string>>(localEvent.RecurrenceJson);
        }

        if (localEvent.OriginalStart is { } originalStart)
        {
            googleEvent.OriginalStartTime = ToEventDateTime(originalStart, localEvent.IsAllDay, localEvent.StartTimeZoneId);
        }

        return googleEvent;
    }

    private static GoogleReminderMetadata CreateReminderMetadata(
        Event.RemindersData? reminders,
        IReadOnlyList<GoogleReminderOverride>? defaultReminders,
        bool adoptEmailRemindersAsLocalNotifications)
    {
        _ = adoptEmailRemindersAsLocalNotifications;
        var metadata = new GoogleReminderMetadata
        {
            UseDefault = reminders?.UseDefault,
            Source = reminders?.UseDefault == true ? "default" : "explicit"
        };
        foreach (var item in reminders?.Overrides ?? [])
        {
            AddReminder(metadata.PopupMinutes, metadata.EmailMinutes, item.Method, item.Minutes);
        }

        foreach (var item in defaultReminders ?? [])
        {
            AddReminder(metadata.DefaultPopupMinutes, metadata.DefaultEmailMinutes, item.Method, item.Minutes);
        }

        var useDefault = reminders?.UseDefault == true;
        var popupSource = useDefault ? metadata.DefaultPopupMinutes : metadata.PopupMinutes;
        if (popupSource.Count > 0)
        {
            metadata.AdoptedReminderMinutes = popupSource.Min();
            metadata.AdoptedReminderMethod = useDefault ? "default-popup" : "popup";
        }
        if (reminders?.UseDefault == true && defaultReminders is null)
        {
            metadata.Source = "default-unavailable";
        }

        return metadata;
    }

    private static void AddReminder(ICollection<int> popup, ICollection<int> email, string? method, int? minutes)
    {
        if (minutes is null)
        {
            return;
        }

        if (string.Equals(method, "popup", StringComparison.OrdinalIgnoreCase))
        {
            popup.Add(minutes.Value);
        }
        else if (string.Equals(method, "email", StringComparison.OrdinalIgnoreCase))
        {
            email.Add(minutes.Value);
        }
    }

    private static Event.RemindersData ToGoogleReminders(LocalEvent localEvent)
    {
        if (localEvent.IsTodoLike)
        {
            return TodoReminderPolicy.CreateGoogleRemindersDisabled();
        }

        if (localEvent.GoogleReminderMetadata?.UseDefault == true)
        {
            return new Event.RemindersData
            {
                UseDefault = true,
                Overrides = []
            };
        }

        var reminders = new Event.RemindersData
        {
            UseDefault = false,
            Overrides = []
        };
        var overrides = new List<EventReminder>();
        foreach (var minutes in localEvent.EffectiveAppReminderMinutesBeforeStart)
        {
            overrides.Add(new EventReminder
            {
                Method = "popup",
                Minutes = minutes
            });
        }

        foreach (var minutes in GetGoogleEmailReminderMinutesForExport(localEvent))
        {
            overrides.Add(new EventReminder
            {
                Method = "email",
                Minutes = minutes
            });
        }

        reminders.Overrides = overrides;
        return reminders;
    }

    private static bool HasPopupReminder(GoogleReminderMetadata metadata)
    {
        return metadata.UseDefault == true
            ? metadata.DefaultPopupMinutes.Count > 0
            : metadata.PopupMinutes.Count > 0;
    }

    private static bool HasEmailReminder(GoogleReminderMetadata metadata)
    {
        return metadata.UseDefault == true
            ? metadata.DefaultEmailMinutes.Count > 0
            : metadata.EmailMinutes.Count > 0;
    }

    private static IReadOnlyList<int> GetPopupReminderMinutes(GoogleReminderMetadata metadata)
    {
        var source = metadata.UseDefault == true ? metadata.DefaultPopupMinutes : metadata.PopupMinutes;
        return CalendarEvent.NormalizeReminderMinutes(source);
    }

    private static IReadOnlyList<int> GetEmailReminderMinutes(GoogleReminderMetadata metadata)
    {
        var source = metadata.UseDefault == true ? metadata.DefaultEmailMinutes : metadata.EmailMinutes;
        return CalendarEvent.NormalizeReminderMinutes(source);
    }

    private static IReadOnlyList<int> GetGoogleEmailReminderMinutesForExport(LocalEvent localEvent)
    {
        var configured = CalendarEvent.NormalizeReminderMinutes(localEvent.GoogleEmailReminderMinutesBeforeStart);
        if (configured.Count > 0)
        {
            return configured;
        }

        return localEvent.GoogleEmailReminderEnabled == true && localEvent.ReminderMinutesBeforeStart is int minutes
            ? [minutes]
            : [];
    }

    private static int? FirstOrDefaultOrNull(IReadOnlyList<int> values)
    {
        return values.Count == 0 ? null : values[0];
    }

    private static DateTimeOffset ParseEventDateTime(EventDateTime? value, bool isAllDay)
    {
        if (value is null)
        {
            return DateTimeOffset.Now;
        }

        if (isAllDay && DateTime.TryParse(value.Date, out var date))
        {
            return new DateTimeOffset(date.Date);
        }

        return value.DateTimeDateTimeOffset ?? DateTimeOffset.Now;
    }

    private static EventDateTime ToEventDateTime(DateTimeOffset value, bool isAllDay, string? timeZoneId)
    {
        if (isAllDay)
        {
            return new EventDateTime { Date = value.Date.ToString("yyyy-MM-dd") };
        }

        return new EventDateTime
        {
            DateTimeDateTimeOffset = value,
            TimeZone = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId
        };
    }
}
