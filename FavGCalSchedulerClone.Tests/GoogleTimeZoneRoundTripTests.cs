using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Google.Apis.Calendar.v3.Data;

namespace FavGCalSchedulerClone.Tests;

public sealed class GoogleTimeZoneRoundTripTests
{
    [Fact]
    public void Mapper_RoundTripsSourceGoogleTimeZone()
    {
        var google = new Event
        {
            Id = "ny-event",
            Summary = "New York meeting",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(-5)),
                TimeZone = "America/New_York"
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(-5)),
                TimeZone = "America/New_York"
            },
            Status = "confirmed"
        };

        var local = GoogleEventMapper.FromGoogleEvent(google, "work");
        local.Title = "Edited title";
        var roundTripped = GoogleEventMapper.ToGoogleEvent(local);

        Assert.Equal("America/New_York", local.StartTimeZoneId);
        Assert.Equal("America/New_York", local.EndTimeZoneId);
        Assert.Equal("America/New_York", roundTripped.Start.TimeZone);
        Assert.Equal("America/New_York", roundTripped.End.TimeZone);
    }

    [Fact]
    public async Task Repository_PersistsEventTimeZoneIds()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var local = new CalendarEvent
        {
            Id = "timezone-persist",
            CalendarId = "work",
            Title = "Persist timezone",
            Start = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(-5)),
            End = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(-5)),
            StartTimeZoneId = "America/New_York",
            EndTimeZoneId = "America/New_York"
        };

        await repository.SaveEventAsync(local);
        var stored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(local.Id));

        Assert.Equal("America/New_York", stored.StartTimeZoneId);
        Assert.Equal("America/New_York", stored.EndTimeZoneId);
    }
}
