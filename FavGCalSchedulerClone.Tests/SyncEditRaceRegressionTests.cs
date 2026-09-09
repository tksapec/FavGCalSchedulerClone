using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class SyncEditRaceRegressionTests
{
    [Fact]
    public async Task MarkSyncedAsync_PreservesNewerDirtyEdit_WhenOlderSyncSnapshotCompletes()
    {
        var repository = await CreateRepositoryAsync();
        var local = CreateEvent("existing-race", "google-existing", "etag-1");
        local.IsDirty = false;
        await repository.SaveEventAsync(local);

        var firstEdit = (await repository.FindEventByIdAsync(local.Id))!;
        firstEdit.Title = "First edit";
        firstEdit.AppReminderMinutesBeforeStart = [5];
        firstEdit.ReminderMinutesBeforeStart = 5;
        firstEdit.IsAppReminderEnabled = true;
        firstEdit.IsDirty = true;
        await repository.SaveEventAsync(firstEdit);
        var staleSyncSnapshot = (await repository.FindEventByIdAsync(local.Id))!;

        var newerEdit = (await repository.FindEventByIdAsync(local.Id))!;
        newerEdit.Title = "Second edit";
        newerEdit.AppReminderMinutesBeforeStart = [15];
        newerEdit.ReminderMinutesBeforeStart = 15;
        newerEdit.IsAppReminderEnabled = true;
        newerEdit.IsDirty = true;
        await repository.SaveEventAsync(newerEdit);

        await repository.MarkSyncedAsync(staleSyncSnapshot, lastSyncedGoogleEtag: "etag-2");

        var stored = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.Equal("Second edit", stored.Title);
        Assert.True(stored.IsDirty);
        Assert.False(string.IsNullOrWhiteSpace(stored.DirtyFields));
        Assert.Equal([15], stored.EffectiveAppReminderMinutesBeforeStart);
        Assert.Equal("etag-2", stored.LastSyncedGoogleEtag);
    }

    [Fact]
    public async Task MarkSyncedAsync_PreservesNewerDirtyEdit_AndStoresRemoteIdentity_WhenInitialInsertCompletes()
    {
        var repository = await CreateRepositoryAsync();
        var local = CreateEvent("insert-race", googleEventId: null, etag: null);
        local.IsDirty = true;
        await repository.SaveEventAsync(local);
        var staleInsertSnapshot = (await repository.FindEventByIdAsync(local.Id))!;

        var newerEdit = (await repository.FindEventByIdAsync(local.Id))!;
        newerEdit.Title = "Edited while insert was running";
        newerEdit.IsDirty = true;
        await repository.SaveEventAsync(newerEdit);

        await repository.MarkSyncedAsync(
            staleInsertSnapshot,
            googleEventId: "google-created",
            lastSyncedGoogleEtag: "etag-created");

        var stored = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.Equal("Edited while insert was running", stored.Title);
        Assert.True(stored.IsDirty);
        Assert.False(string.IsNullOrWhiteSpace(stored.DirtyFields));
        Assert.Equal("google-created", stored.GoogleEventId);
        Assert.Equal("etag-created", stored.LastSyncedGoogleEtag);
    }

    [Fact]
    public async Task SaveEventAsync_PreservesLatestSyncMetadata_WhenEditorStartedBeforeSyncCompleted()
    {
        var repository = await CreateRepositoryAsync();
        var local = CreateEvent("editor-race", "google-editor", "etag-1");
        local.IsDirty = false;
        await repository.SaveEventAsync(local);

        var staleEditorSnapshot = (await repository.FindEventByIdAsync(local.Id))!;
        var syncSnapshot = (await repository.FindEventByIdAsync(local.Id))!;
        await repository.MarkSyncedAsync(syncSnapshot, lastSyncedGoogleEtag: "etag-2");
        var syncedState = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.NotNull(syncedState.LastSyncedAt);

        staleEditorSnapshot.Title = "Edited after sync completed";
        staleEditorSnapshot.IsDirty = true;
        await repository.SaveEventAsync(staleEditorSnapshot);

        var stored = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.True(stored.IsDirty);
        Assert.Equal("etag-2", stored.LastSyncedGoogleEtag);
        Assert.Equal(syncedState.LastSyncedAt, stored.LastSyncedAt);
    }

    private static async Task<CalendarRepository> CreateRepositoryAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        return repository;
    }

    private static CalendarEvent CreateEvent(string id, string? googleEventId, string? etag)
    {
        return new CalendarEvent
        {
            Id = id,
            GoogleEventId = googleEventId,
            LastSyncedGoogleEtag = etag,
            CalendarId = "primary",
            Title = "Original",
            Start = new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.FromHours(9)),
            IsAllDay = false,
            IsDeleted = false
        };
    }
}
