using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using Google.Apis.Calendar.v3.Data;
using LocalEvent = FavGCalSchedulerClone.App.Models.CalendarEvent;

namespace FavGCalSchedulerClone.App.Services;

public static class GoogleEventMapper
{
    public static LocalEvent FromGoogleEvent(Event googleEvent, string calendarId)
    {
        var isAllDay = googleEvent.Start?.Date is { Length: > 0 };
        var start = ParseEventDateTime(googleEvent.Start, isAllDay);
        var end = ParseEventDateTime(googleEvent.End, isAllDay);

        var local = new LocalEvent
        {
            Id = string.IsNullOrWhiteSpace(googleEvent.Id) ? Guid.NewGuid().ToString("N") : $"g:{calendarId}:{googleEvent.Id}",
            GoogleEventId = googleEvent.Id,
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
            IsAllDay = isAllDay,
            ColorId = googleEvent.ColorId,
            RecurrenceJson = googleEvent.Recurrence is null ? null : JsonSerializer.Serialize(googleEvent.Recurrence),
            IsDeleted = string.Equals(googleEvent.Status, "cancelled", StringComparison.OrdinalIgnoreCase),
            UpdatedAt = googleEvent.UpdatedDateTimeOffset ?? DateTimeOffset.Now,
            IsDirty = false
        };
        local.IsTodoLike = TagService.IsTodoLike(local);
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
            Start = ToEventDateTime(localEvent.Start, localEvent.IsAllDay),
            End = ToEventDateTime(localEvent.End, localEvent.IsAllDay),
            Status = localEvent.IsDeleted ? "cancelled" : "confirmed"
        };

        if (!string.IsNullOrWhiteSpace(localEvent.RecurrenceJson))
        {
            googleEvent.Recurrence = JsonSerializer.Deserialize<IList<string>>(localEvent.RecurrenceJson);
        }

        if (localEvent.OriginalStart is { } originalStart)
        {
            googleEvent.OriginalStartTime = ToEventDateTime(originalStart, localEvent.IsAllDay);
        }

        return googleEvent;
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

    private static EventDateTime ToEventDateTime(DateTimeOffset value, bool isAllDay)
    {
        if (isAllDay)
        {
            return new EventDateTime { Date = value.Date.ToString("yyyy-MM-dd") };
        }

        return new EventDateTime
        {
            DateTimeDateTimeOffset = value,
            TimeZone = GoogleCalendarTimeZone.LocalIanaId
        };
    }
}
