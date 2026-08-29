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
    public void Mapper_InheritsStartTimeZoneWhenGoogleEndOmitsTimeZone()
    {
        var google = new Event
        {
            Id = "ny-end-zone-omitted",
            Summary = "New York meeting",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.FromHours(-4)),
                TimeZone = "America/New_York"
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.FromHours(-4))
            },
            Status = "confirmed"
        };

        var local = GoogleEventMapper.FromGoogleEvent(google, "work");
        var roundTripped = GoogleEventMapper.ToGoogleEvent(local);

        Assert.Equal("America/New_York", local.StartTimeZoneId);
        Assert.Equal("America/New_York", local.EndTimeZoneId);
        Assert.Equal("America/New_York", roundTripped.End.TimeZone);
    }

    [Fact]
    public void Mapper_DoesNotInventLocalTimeZoneWhenGoogleSuppliesOnlyExplicitOffset()
    {
        var google = new Event
        {
            Id = "offset-only-event",
            Summary = "Offset only",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.FromHours(-4))
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.FromHours(-4))
            },
            Status = "confirmed"
        };

        var local = GoogleEventMapper.FromGoogleEvent(google, "work");
        var roundTripped = GoogleEventMapper.ToGoogleEvent(local);

        Assert.Null(local.StartTimeZoneId);
        Assert.Null(local.EndTimeZoneId);
        Assert.Null(roundTripped.Start.TimeZone);
        Assert.Null(roundTripped.End.TimeZone);
        Assert.Equal(TimeSpan.FromHours(-4), roundTripped.Start.DateTimeDateTimeOffset!.Value.Offset);
        Assert.Equal(TimeSpan.FromHours(-4), roundTripped.End.DateTimeDateTimeOffset!.Value.Offset);
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

    [Fact]
    public void RecurrenceExpansion_RetainsTimeZoneAndDoesNotShareGoogleMetadata()
    {
        var master = new CalendarEvent
        {
            Id = "timezone-series",
            CalendarId = "work",
            Title = "NY series",
            Start = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(-5)),
            End = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(-5)),
            StartTimeZoneId = "America/New_York",
            EndTimeZoneId = "America/New_York",
            RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=2\"]",
            GoogleReminderMetadata = new GoogleReminderMetadata { UseDefault = true, Source = "default" }
        };

        var generated = RecurrenceExpansionService.ExpandForRange(
            [master],
            new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.FromHours(-5)));

        Assert.Equal(2, generated.Count);
        Assert.All(generated, item => Assert.Equal("America/New_York", item.StartTimeZoneId));
        generated[0].GoogleReminderMetadata!.Source = "changed";
        Assert.Equal("default", master.GoogleReminderMetadata!.Source);
    }

    [Fact]
    public void RecurrenceExpansion_RecalculatesOffsetAcrossDaylightSavingBoundary()
    {
        var master = new CalendarEvent
        {
            Id = "dst-series",
            CalendarId = "work",
            Title = "DST series",
            Start = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.FromHours(-5)),
            End = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.FromHours(-5)),
            StartTimeZoneId = "America/New_York",
            EndTimeZoneId = "America/New_York",
            RecurrenceJson = "[\"RRULE:FREQ=WEEKLY;COUNT=2\"]"
        };

        var generated = RecurrenceExpansionService.ExpandForRange(
            [master],
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.FromHours(-4)));

        Assert.Equal(2, generated.Count);
        Assert.Equal(TimeSpan.FromHours(-5), generated[0].Start.Offset);
        Assert.Equal(TimeSpan.FromHours(-4), generated[1].Start.Offset);
        Assert.All(generated, item => Assert.Equal(9, item.Start.Hour));
    }

    [Theory]
    [InlineData(2026, 1, 15, -5)]
    [InlineData(2026, 7, 15, -4)]
    public void TryCreateDateTimeOffset_UsesEventTimeZoneForEditedWallClock(int year, int month, int day, int expectedOffsetHours)
    {
        var wallClock = new DateTime(year, month, day, 10, 30, 0, DateTimeKind.Unspecified);

        var created = GoogleCalendarTimeZone.TryCreateDateTimeOffset(
            wallClock,
            "America/New_York",
            preferredOffset: TimeSpan.FromHours(-5),
            out var result);

        Assert.True(created);
        Assert.Equal(10, result.Hour);
        Assert.Equal(30, result.Minute);
        Assert.Equal(TimeSpan.FromHours(expectedOffsetHours), result.Offset);
    }

    [Fact]
    public void TryCreateDateTimeOffset_WithoutTimeZoneKeepsPreferredExistingOffset()
    {
        var wallClock = new DateTime(2026, 7, 15, 10, 30, 0, DateTimeKind.Unspecified);

        var created = GoogleCalendarTimeZone.TryCreateDateTimeOffset(
            wallClock,
            timeZoneId: null,
            preferredOffset: TimeSpan.FromHours(-4),
            out var result);

        Assert.True(created);
        Assert.Equal(TimeSpan.FromHours(-4), result.Offset);
        Assert.Equal(10, result.Hour);
        Assert.Equal(30, result.Minute);
    }

    [Fact]
    public void TryCreateDateTimeOffset_RejectsNonexistentDstWallClock()
    {
        var nonexistent = new DateTime(2026, 3, 8, 2, 30, 0, DateTimeKind.Unspecified);

        var created = GoogleCalendarTimeZone.TryCreateDateTimeOffset(
            nonexistent,
            "America/New_York",
            preferredOffset: TimeSpan.FromHours(-5),
            out _);

        Assert.False(created);
    }
}
