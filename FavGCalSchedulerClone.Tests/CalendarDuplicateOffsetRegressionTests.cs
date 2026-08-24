using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarDuplicateOffsetRegressionTests
{
    [Fact]
    public async Task FindDuplicateEventAsync_MatchesSameTimedInstantWithDifferentOffsets()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var stored = new CalendarEvent
        {
            Id = "offset-duplicate-source",
            CalendarId = "work",
            Title = "Offset duplicate",
            Location = "Room A",
            Start = new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.FromHours(9))
        };
        await repository.SaveEventAsync(stored);
        var candidate = new CalendarEvent
        {
            CalendarId = "work",
            Title = stored.Title,
            Location = stored.Location,
            Start = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 2, 1, 0, 0, TimeSpan.Zero)
        };

        var duplicate = await repository.FindDuplicateEventAsync(candidate);

        Assert.NotNull(duplicate);
        Assert.Equal(stored.Id, duplicate!.Id);
    }

    [Fact]
    public async Task FindDuplicateEventAsync_MatchesAllDayDatesRegardlessOfOffset()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var stored = new CalendarEvent
        {
            Id = "all-day-offset-source",
            CalendarId = "work",
            Title = "All day duplicate",
            IsAllDay = true,
            Start = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.FromHours(9))
        };
        await repository.SaveEventAsync(stored);
        var candidate = new CalendarEvent
        {
            CalendarId = "work",
            Title = stored.Title,
            IsAllDay = true,
            Start = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero)
        };

        var duplicate = await repository.FindDuplicateEventAsync(candidate);

        Assert.NotNull(duplicate);
        Assert.Equal(stored.Id, duplicate!.Id);
    }
}
