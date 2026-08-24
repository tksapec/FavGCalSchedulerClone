using System.Reflection;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class BackupRestoreConcurrencyRegressionTests
{
    [Fact]
    public async Task RestoreAllCalendarsAsync_RejectsRestoreWhileGoogleSyncIsRunning()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        var syncField = typeof(MainViewModel).GetField("_syncInProgress", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(syncField);
        syncField!.SetValue(viewModel, 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.RestoreAllCalendarsAsync(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.zip")));

        Assert.Contains("同期", exception.Message, StringComparison.Ordinal);
    }
}
