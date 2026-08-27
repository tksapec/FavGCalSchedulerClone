using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class MonthBoundaryNavigationRegressionTests
{
    [Fact]
    public async Task NextMonth_FromNextMonthGridDay_MovesToNextDisplayedMonthOnly()
    {
        var viewModel = await CreateViewModelAsync();
        await viewModel.NavigateToDateAsync(new DateTime(2026, 8, 15));
        viewModel.SelectedDay = viewModel.CalendarDays.Single(day => day.Date == new DateTime(2026, 9, 2));

        viewModel.NextMonthCommand.Execute(null);

        Assert.Equal(new DateTime(2026, 9, 1), viewModel.CurrentMonth);
    }

    [Fact]
    public async Task PreviousMonth_FromPreviousMonthGridDay_MovesToPreviousDisplayedMonthOnly()
    {
        var viewModel = await CreateViewModelAsync();
        await viewModel.NavigateToDateAsync(new DateTime(2026, 8, 15));
        viewModel.SelectedDay = viewModel.CalendarDays.Single(day => day.Date == new DateTime(2026, 7, 30));

        viewModel.PreviousMonthCommand.Execute(null);

        Assert.Equal(new DateTime(2026, 7, 1), viewModel.CurrentMonth);
    }

    private static async Task<MainViewModel> CreateViewModelAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        return viewModel;
    }
}
