using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class SyncEditRaceRegressionTests
{
    [Fact]
    public async Task SaveEventAsync_AssignsRevisionAfterExistingTimestampWhenClockIsEarlier()
    {
        var repository = await CreateRepositoryAsync();
        var existing = CreateEvent("monotonic-save", "google-monotonic-save", "etag-1");
        existing.UpdatedAt = DateTimeOffset.Now.AddMinutes(1);
        await repository.UpsertSyncedEventAsync(existing);

        var edited = CreateEvent(existing.Id, existing.GoogleEventId, existing.LastSyncedGoogleEtag);
        edited.Title = "Edited after a future persisted timestamp";
        await repository.SaveEventAsync(edited);

        var stored = (await repository.FindEventByIdAsync(existing.Id))!;
        Assert.True(stored.UpdatedAt > existing.UpdatedAt);
    }

    [Fact]
    public async Task AtomicSave_AssignsRevisionAfterExistingTimestampWhenClockIsEarlier()
    {
        var repository = await CreateRepositoryAsync();
        var existing = CreateEvent("monotonic-atomic", "google-monotonic-atomic", "etag-1");
        existing.UpdatedAt = DateTimeOffset.Now.AddMinutes(1);
        await repository.UpsertSyncedEventAsync(existing);

        var edited = CreateEvent(existing.Id, existing.GoogleEventId, existing.LastSyncedGoogleEtag);
        edited.Description = "#todo[priority:A][progress:50]";
        edited.IsTodoLike = true;
        await CalendarRepositoryAtomicWriter.SaveEventsAsync(repository, [edited]);

        var stored = (await repository.FindEventByIdAsync(existing.Id))!;
        Assert.True(stored.UpdatedAt > existing.UpdatedAt);
    }

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

    [Fact]
    public async Task AtomicSave_PreservesLatestSyncMetadata_WhenTodoEditorStartedBeforeSyncCompleted()
    {
        var repository = await CreateRepositoryAsync();
        var local = CreateEvent("todo-editor-race", "google-todo", "etag-1");
        local.Description = "#todo[priority:A][progress:0]";
        local.IsTodoLike = true;
        local.IsAllDay = true;
        local.Start = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        local.End = local.Start.AddDays(1);
        local.IsDirty = false;
        await repository.SaveEventAsync(local);

        var staleEditorSnapshot = (await repository.FindEventByIdAsync(local.Id))!;
        var syncSnapshot = (await repository.FindEventByIdAsync(local.Id))!;
        await repository.MarkSyncedAsync(syncSnapshot, lastSyncedGoogleEtag: "etag-2");
        var syncedState = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.NotNull(syncedState.LastSyncedAt);

        staleEditorSnapshot.Description = "#todo[priority:A][progress:50]";
        staleEditorSnapshot.IsDirty = true;
        await CalendarRepositoryAtomicWriter.SaveEventsAsync(repository, [staleEditorSnapshot]);

        var stored = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.True(stored.IsDirty);
        Assert.Equal("etag-2", stored.LastSyncedGoogleEtag);
        Assert.Equal(syncedState.LastSyncedAt, stored.LastSyncedAt);
    }

    [Fact]
    public async Task SaveEventAsync_PreservesRecreatedRemoteIdentity_WhenEditorSnapshotHasOldGoogleId()
    {
        var repository = await CreateRepositoryAsync();
        var local = CreateEvent("editor-recreate-race", "google-old", "etag-old");
        local.IsDirty = false;
        await repository.SaveEventAsync(local);

        var staleEditorSnapshot = (await repository.FindEventByIdAsync(local.Id))!;
        var syncSnapshot = (await repository.FindEventByIdAsync(local.Id))!;
        await repository.MarkSyncedAsync(
            syncSnapshot,
            googleEventId: "google-recreated",
            lastSyncedGoogleEtag: "etag-recreated");

        staleEditorSnapshot.Title = "Edited after remote recreation";
        staleEditorSnapshot.IsDirty = true;
        await repository.SaveEventAsync(staleEditorSnapshot);

        var stored = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.Equal("google-recreated", stored.GoogleEventId);
        Assert.Equal("etag-recreated", stored.LastSyncedGoogleEtag);
        Assert.True(stored.IsDirty);
        Assert.Equal("Edited after remote recreation", stored.Title);
    }

    [Fact]
    public async Task AtomicSave_PreservesRecreatedRemoteIdentity_WhenTodoEditorSnapshotHasOldGoogleId()
    {
        var repository = await CreateRepositoryAsync();
        var local = CreateEvent("todo-recreate-race", "google-old-todo", "etag-old");
        local.Description = "#todo[priority:A][progress:0]";
        local.IsTodoLike = true;
        local.IsAllDay = true;
        local.Start = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        local.End = local.Start.AddDays(1);
        local.IsDirty = false;
        await repository.SaveEventAsync(local);

        var staleEditorSnapshot = (await repository.FindEventByIdAsync(local.Id))!;
        var syncSnapshot = (await repository.FindEventByIdAsync(local.Id))!;
        await repository.MarkSyncedAsync(
            syncSnapshot,
            googleEventId: "google-recreated-todo",
            lastSyncedGoogleEtag: "etag-recreated-todo");

        staleEditorSnapshot.Description = "#todo[priority:A][progress:50]";
        staleEditorSnapshot.IsDirty = true;
        await CalendarRepositoryAtomicWriter.SaveEventsAsync(repository, [staleEditorSnapshot]);

        var stored = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.Equal("google-recreated-todo", stored.GoogleEventId);
        Assert.Equal("etag-recreated-todo", stored.LastSyncedGoogleEtag);
        Assert.True(stored.IsDirty);
        Assert.Contains("progress:50", stored.Description ?? string.Empty, StringComparison.Ordinal);
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
