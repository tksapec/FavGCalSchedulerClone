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
            Summary = "祝日 #holiday",
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
            Title = "打合ぁE#work",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero),
            IsAllDay = false
        };

        var googleEvent = GoogleEventMapper.ToGoogleEvent(local);

        Assert.Equal("打合ぁE#work", googleEvent.Summary);
        Assert.NotNull(googleEvent.Start.DateTimeDateTimeOffset);
        Assert.NotNull(googleEvent.End.DateTimeDateTimeOffset);
    }
}
