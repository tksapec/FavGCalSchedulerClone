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
}
