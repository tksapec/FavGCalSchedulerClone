using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class GoogleCalendarSyncServiceTests
{
    [Fact]
    public void ResolveNotFoundAction_TreatsDeletedEventAsAlreadySynced()
    {
        var action = GoogleCalendarSyncService.ResolveNotFoundAction(new CalendarEvent { IsDeleted = true });

        Assert.Equal(GoogleNotFoundSyncAction.MarkLocalSynced, action);
    }

    [Fact]
    public void ResolveNotFoundAction_RecreatesRemoteForLocalEdit()
    {
        var action = GoogleCalendarSyncService.ResolveNotFoundAction(new CalendarEvent { IsDeleted = false });

        Assert.Equal(GoogleNotFoundSyncAction.RecreateRemote, action);
    }

    [Fact]
    public async Task LoadCachedEventColorPaletteAsync_ReturnsSavedGoogleColors()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        await repository.SaveSettingValueAsync(
            "google-event-color-palette",
            """{"5":{"Background":"#123456","Foreground":"#FEDCBA"}}""");
        var service = new GoogleCalendarSyncService(repository);

        var palette = await service.LoadCachedEventColorPaletteAsync();

        Assert.Equal("#123456", palette["5"].Background);
        Assert.Equal("#FEDCBA", palette["5"].Foreground);
    }

    [Theory]
    [InlineData(SyncConflictPolicy.SkipLocalDirty, false)]
    [InlineData(SyncConflictPolicy.PreferLocal, false)]
    [InlineData(SyncConflictPolicy.PreferGoogle, true)]
    public void ShouldApplyRemoteChange_ProtectsDirtyLocalEventsByDefault(SyncConflictPolicy policy, bool expected)
    {
        var local = new CalendarEvent { IsDirty = true };

        var apply = GoogleCalendarSyncService.ShouldApplyRemoteChange(local, policy);

        Assert.Equal(expected, apply);
    }

    [Fact]
    public void ShouldApplyRemoteChange_AllowsCleanLocalEvents()
    {
        var local = new CalendarEvent { IsDirty = false };

        Assert.True(GoogleCalendarSyncService.ShouldApplyRemoteChange(local, SyncConflictPolicy.SkipLocalDirty));
    }

    [Fact]
    public async Task RecordFailedSyncAsync_AlwaysStoresLastResult()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var service = new GoogleCalendarSyncService(repository);

        await service.RecordFailedSyncAsync("network failure", keepHistory: false);
        var diagnostics = await service.LoadDiagnosticsAsync(new AppSettings());

        Assert.NotNull(diagnostics.LastResult);
        Assert.Equal(1, diagnostics.LastResult.Failed);
        Assert.Equal("network failure", diagnostics.LastResult.Message);
        Assert.Single(diagnostics.History);
    }

    [Fact]
    public async Task RecordFailedSyncAsync_KeepsHistoryWhenEnabled()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var service = new GoogleCalendarSyncService(repository);

        await service.RecordFailedSyncAsync("first", keepHistory: true);
        await service.RecordFailedSyncAsync("second", keepHistory: true);
        var diagnostics = await service.LoadDiagnosticsAsync(new AppSettings());

        Assert.Equal(2, diagnostics.History.Count);
        Assert.Equal("second", diagnostics.LastResult?.Message);
    }
}
