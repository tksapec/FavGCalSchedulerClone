using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Google.Apis.Calendar.v3.Data;

namespace FavGCalSchedulerClone.Tests;

public sealed class GoogleEventMapperTests
{
    [Fact]
    public void FromGoogleEvent_MapsAllDayEventAsExclusiveEnd()
    {
        var googleEvent = new Event
        {
            Id = "abc",
            Summary = "Holiday #holiday",
            Description = "#holiday",
            Start = new EventDateTime { Date = "2026-05-16" },
            End = new EventDateTime { Date = "2026-05-17" }
        };

        var local = GoogleEventMapper.FromGoogleEvent(googleEvent, "primary");

        Assert.True(local.IsAllDay);
        Assert.Equal(new DateTime(2026, 5, 16), local.Start.Date);
        Assert.Equal(new DateTime(2026, 5, 17), local.End.Date);
        Assert.True(TagService.IsHoliday(local));
    }

    [Fact]
    public void ToGoogleEvent_MapsTimedEvent()
    {
        var local = new App.Models.CalendarEvent
        {
            Title = "Meeting #work",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero),
            IsAllDay = false
        };

        var googleEvent = GoogleEventMapper.ToGoogleEvent(local);

        Assert.Equal("Meeting #work", googleEvent.Summary);
        Assert.NotNull(googleEvent.Start.DateTimeDateTimeOffset);
        Assert.NotNull(googleEvent.End.DateTimeDateTimeOffset);
        Assert.Equal(GoogleCalendarTimeZone.TokyoIanaId, googleEvent.Start.TimeZone);
        Assert.Equal(GoogleCalendarTimeZone.TokyoIanaId, googleEvent.End.TimeZone);
    }

    [Fact]
    public void ToGoogleEvent_MapsRecurringOriginalStartWithIanaTimeZone()
    {
        var local = new App.Models.CalendarEvent
        {
            Title = "Moved meeting",
            Start = new DateTimeOffset(2026, 5, 16, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero),
            OriginalStart = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero),
            IsAllDay = false
        };

        var googleEvent = GoogleEventMapper.ToGoogleEvent(local);

        Assert.Equal(GoogleCalendarTimeZone.TokyoIanaId, googleEvent.OriginalStartTime?.TimeZone);
    }

    [Fact]
    public void FromGoogleEvent_MapsRecurringExceptionMetadata()
    {
        var googleEvent = new Event
        {
            Id = "instance-1",
            RecurringEventId = "series-1",
            OriginalStartTime = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero)
            },
            Summary = "Moved meeting",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 5, 16, 11, 0, 0, TimeSpan.Zero)
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero)
            }
        };

        var local = GoogleEventMapper.FromGoogleEvent(googleEvent, "primary");

        Assert.True(local.IsRecurrenceException);
        Assert.Equal("series-1", local.RecurringEventId);
        Assert.Equal(new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero), local.OriginalStart);
    }

    [Fact]
    public void FromGoogleEvent_UsesNearestPopupReminder()
    {
        var googleEvent = CreateTimedGoogleEvent();
        googleEvent.Reminders = new Event.RemindersData
        {
            UseDefault = false,
            Overrides =
            [
                new EventReminder { Method = "popup", Minutes = 30 },
                new EventReminder { Method = "popup", Minutes = 10 },
                new EventReminder { Method = "email", Minutes = 5 }
            ]
        };

        var local = GoogleEventMapper.FromGoogleEvent(googleEvent, "primary");

        Assert.Equal(10, local.ReminderMinutesBeforeStart);
        Assert.Equal([10, 30], local.GoogleReminderMetadata!.PopupMinutes.Order().ToArray());
        Assert.Equal([5], local.GoogleReminderMetadata.EmailMinutes);
    }

    [Fact]
    public void FromGoogleEvent_DoesNotUseEmailOnlyReminderAsLocalPopupReminder()
    {
        var googleEvent = CreateTimedGoogleEvent();
        googleEvent.Reminders = new Event.RemindersData
        {
            UseDefault = false,
            Overrides = [new EventReminder { Method = "email", Minutes = 30 }]
        };

        var local = GoogleEventMapper.FromGoogleEvent(googleEvent, "primary");

        Assert.Null(local.ReminderMinutesBeforeStart);
        Assert.True(local.GoogleReminderMetadata!.HasEmailOnly);
        Assert.Equal([30], local.GoogleReminderMetadata.EmailMinutes);
    }

    [Fact]
    public void FromGoogleEvent_UsesDefaultPopupReminderWhenAvailable()
    {
        var googleEvent = CreateTimedGoogleEvent();
        googleEvent.Reminders = new Event.RemindersData { UseDefault = true };

        var local = GoogleEventMapper.FromGoogleEvent(
            googleEvent,
            "primary",
            [new GoogleReminderOverride("email", 60), new GoogleReminderOverride("popup", 15)]);

        Assert.Equal(15, local.ReminderMinutesBeforeStart);
        Assert.True(local.GoogleReminderMetadata!.UseDefault);
        Assert.Equal([15], local.GoogleReminderMetadata.DefaultPopupMinutes);
        Assert.Equal([60], local.GoogleReminderMetadata.DefaultEmailMinutes);
    }

    [Fact]
    public void FromGoogleEvent_DoesNotTreatUnavailableDefaultsAsNoReminder()
    {
        var googleEvent = CreateTimedGoogleEvent();
        googleEvent.Reminders = new Event.RemindersData { UseDefault = true };

        var local = GoogleEventMapper.FromGoogleEvent(googleEvent, "primary", defaultReminders: null);

        Assert.Null(local.ReminderMinutesBeforeStart);
        Assert.True(local.GoogleReminderMetadata!.UseDefault);
        Assert.Equal("default-unavailable", local.GoogleReminderMetadata.Source);
    }

    [Fact]
    public void ToGoogleEvent_WritesSinglePopupReminder()
    {
        var local = new App.Models.CalendarEvent
        {
            Title = "Reminder",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero),
            ReminderMinutesBeforeStart = 10
        };

        var googleEvent = GoogleEventMapper.ToGoogleEvent(local);

        Assert.False(googleEvent.Reminders.UseDefault);
        var reminder = Assert.Single(googleEvent.Reminders.Overrides);
        Assert.Equal("popup", reminder.Method);
        Assert.Equal(10, reminder.Minutes);
    }

    [Fact]
    public void ToGoogleEvent_DisablesGoogleDefaultsWhenLocalReminderIsNone()
    {
        var local = new App.Models.CalendarEvent
        {
            Title = "No reminder",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero)
        };

        var googleEvent = GoogleEventMapper.ToGoogleEvent(local);

        Assert.False(googleEvent.Reminders.UseDefault);
        Assert.Empty(googleEvent.Reminders.Overrides);
    }

    private static Event CreateTimedGoogleEvent()
    {
        return new Event
        {
            Id = "event-1",
            Summary = "Reminder source",
            Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero) },
            End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero) }
        };
    }
}
