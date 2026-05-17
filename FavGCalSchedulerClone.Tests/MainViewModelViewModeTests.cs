using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class MainViewModelViewModeTests
{
    [Fact]
    public async Task ViewModeSwitch_ChangesVisibleCalendarDayCount()
    {
        var viewModel = await CreateViewModelAsync();

        Assert.True(viewModel.IsMonthView);
        Assert.Equal(42, viewModel.VisibleCalendarDays.Count);

        viewModel.CurrentViewMode = CalendarViewMode.Week;
        Assert.True(viewModel.IsWeekView);
        Assert.Equal(7, viewModel.VisibleCalendarDays.Count);

        viewModel.CurrentViewMode = CalendarViewMode.Day;
        Assert.True(viewModel.IsDayView);
        Assert.Single(viewModel.VisibleCalendarDays);
    }

    [Fact]
    public async Task GoToTodayAsync_InWeekView_SelectsTodayAndKeepsWeekVisible()
    {
        var viewModel = await CreateViewModelAsync();
        viewModel.CurrentViewMode = CalendarViewMode.Week;
        viewModel.CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-2);

        await viewModel.GoToTodayAsync();

        Assert.True(viewModel.IsWeekView);
        Assert.NotNull(viewModel.SelectedDay);
        Assert.Equal(DateTime.Today, viewModel.SelectedDay.Date);
        Assert.Equal(7, viewModel.VisibleCalendarDays.Count);
        Assert.Contains(viewModel.VisibleCalendarDays, day => day.Date == DateTime.Today);
    }

    [Fact]
    public async Task NavigationCommands_UseWeekAndDayUnits()
    {
        var viewModel = await CreateViewModelAsync();
        var baseline = new DateTime(2026, 5, 15);

        viewModel.CurrentMonth = baseline;
        await Task.Delay(50);
        viewModel.SelectedDay = viewModel.CalendarDays.First(day => day.Date == baseline);

        viewModel.CurrentViewMode = CalendarViewMode.Week;
        viewModel.PreviousMonthCommand.Execute(null);
        await Task.Delay(50);
        Assert.Equal(baseline.AddDays(-7), viewModel.SelectedDay?.Date);

        viewModel.CurrentViewMode = CalendarViewMode.Day;
        viewModel.NextMonthCommand.Execute(null);
        await Task.Delay(50);
        Assert.Equal(baseline.AddDays(-6), viewModel.SelectedDay?.Date);
    }

    private static async Task<MainViewModel> CreateViewModelAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        viewModel.CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        await Task.Delay(50);
        return viewModel;
    }
}
