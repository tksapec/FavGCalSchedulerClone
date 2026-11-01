using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class TrayCloseNotificationSettingsTests
{
    [Fact]
    public async Task FirstCloseNoticeSetting_PersistsAndPreventsRepeatNotice()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(databasePath);
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();

        Assert.False(viewModel.HasShownTrayCloseNotification);
        await viewModel.MarkTrayCloseNotificationShownAsync();

        Assert.True(viewModel.HasShownTrayCloseNotification);
        var persisted = await repository.LoadSettingsAsync();
        Assert.True(persisted.HasShownTrayCloseNotification);

        var reloadedRepository = new CalendarRepository(databasePath);
        var reloadedViewModel = new MainViewModel(reloadedRepository, new GoogleCalendarSyncService(reloadedRepository));
        await reloadedViewModel.InitializeAsync();
        Assert.True(reloadedViewModel.HasShownTrayCloseNotification);
    }
}
