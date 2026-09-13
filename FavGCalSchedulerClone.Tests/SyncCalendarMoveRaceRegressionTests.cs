using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class SyncCalendarMoveRaceRegressionTests
{
    [Fact]
    public async Task MarkSyncedAsync_DoesNotReattachOldRemoteIdentityAfterCalendarMove()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var local = new CalendarEvent
        {
            Id = "calendar-move-race",
            CalendarId = "primary",
            GoogleEventId = "old-remote-id",
            LastSyncedGoogleEtag = "etag-1",
            Title = "Original",
            Start = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.FromHours(9)),
            IsDirty = false
        };
        await repository.UpsertSyncedEventAsync(local);
        var staleSyncSnapshot = (await repository.FindEventByIdAsync(local.Id))!;

        var moved = (await repository.FindEventByIdAsync(local.Id))!;
        moved.CalendarId = "secondary";
        moved.GoogleEventId = null;
        moved.LastSyncedGoogleEtag = null;
        moved.LastSyncedAt = null;
        moved.IsDirty = true;
        await repository.SaveEventAsync(moved);

        await repository.MarkSyncedAsync(
            staleSyncSnapshot,
            googleEventId: "old-remote-id",
            lastSyncedGoogleEtag: "etag-2");

        var stored = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.Equal("secondary", stored.CalendarId);
        Assert.Null(stored.GoogleEventId);
        Assert.Null(stored.LastSyncedGoogleEtag);
        Assert.Null(stored.LastSyncedAt);
        Assert.True(stored.IsDirty);
    }
}
