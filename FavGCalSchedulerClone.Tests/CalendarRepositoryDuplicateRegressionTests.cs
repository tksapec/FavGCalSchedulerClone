using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarRepositoryDuplicateRegressionTests
{
    [Fact]
    public async Task FindDuplicateEventAsync_MatchesSameInstantWithDifferentUtcOffset()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var stored = new CalendarEvent
        {
            Id = "stored-offset",
            CalendarId = "work",
            Title = "Same meeting",
            Location = "Room A",
            Start = new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.FromHours(9)),
            IsDirty = false
        };
        await repository.SaveEventAsync(stored);
        var importedRepresentation = new CalendarEvent
        {
            CalendarId = "work",
            Title = "Same meeting",
            Location = "Room A",
            Start = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 8, 24, 1, 0, 0, TimeSpan.Zero)
        };

        var duplicate = await repository.FindDuplicateEventAsync(importedRepresentation);

        Assert.NotNull(duplicate);
        Assert.Equal(stored.Id, duplicate!.Id);
    }
}
