using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarRepositoryTests
{
    [Fact]
    public async Task UpsertSyncedEventAsync_MergesByGoogleEventId()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();

        var local = new CalendarEvent
        {
            Title = "local",
            CalendarId = "primary",
            GoogleEventId = "google-1",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero),
            IsDirty = false
        };
        await repository.SaveEventAsync(local);

        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "g:primary:google-1",
            Title = "remote",
            CalendarId = "primary",
            GoogleEventId = "google-1",
            Start = local.Start,
            End = local.End
        });

        var events = await repository.LoadEventsAsync(local.Start.AddHours(-1), local.End.AddHours(1));

        Assert.Single(events);
        Assert.Equal(local.Id, events[0].Id);
        Assert.Equal("remote", events[0].Title);
    }
}
