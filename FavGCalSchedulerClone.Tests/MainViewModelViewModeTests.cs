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

    [Fact]
    public async Task SelectedDayChange_RefreshesSelectedDayEventsWithoutRebuildingVisibleDays()
    {
        var selectedDate = DateTime.Today;
        var otherDate = selectedDate.AddDays(1);
        var viewModel = await CreateViewModelAsync([
            CreateEvent("Selected date event", selectedDate),
            CreateEvent("Other date event", otherDate)
        ]);
        var visibleDays = viewModel.VisibleCalendarDays.ToArray();

        viewModel.SelectedDay = viewModel.CalendarDays.First(day => day.Date == otherDate.Date);

        Assert.Equal(visibleDays, viewModel.VisibleCalendarDays);
        Assert.Single(viewModel.SelectedDayEvents);
        Assert.Equal("Other date event", viewModel.SelectedDayEvents[0].Title);
        Assert.Equal($"{otherDate:yyyy/MM/dd} を選択しました。", viewModel.Status);
    }

    [Fact]
    public async Task SelectedDayChange_ClearsSelectedEventWhenEventIsOnDifferentDate()
    {
        var selectedDate = DateTime.Today;
        var otherDate = selectedDate.AddDays(1);
        var selectedEvent = CreateEvent("Selected date event", selectedDate);
        var viewModel = await CreateViewModelAsync([selectedEvent]);

        viewModel.SelectedEvent = selectedEvent;
        viewModel.SelectedDay = viewModel.CalendarDays.First(day => day.Date == otherDate.Date);

        Assert.Null(viewModel.SelectedEvent);
        Assert.Equal(otherDate.Date, viewModel.SelectedDay?.Date);
    }

    [Fact]
    public async Task SelectEvent_SelectsEventAndItsDate()
    {
        var eventDate = DateTime.Today.AddDays(2);
        var calendarEvent = CreateEvent("Target event", eventDate);
        var viewModel = await CreateViewModelAsync([calendarEvent]);

        viewModel.SelectEvent(calendarEvent);

        Assert.Same(calendarEvent, viewModel.SelectedEvent);
        Assert.Equal(eventDate.Date, viewModel.SelectedDay?.Date);
    }

    [Fact]
    public async Task NavigateToDateAsync_SelectsTargetDateWithoutRevertingToToday()
    {
        var targetDate = DateTime.Today.AddMonths(1).AddDays(3);
        var viewModel = await CreateViewModelAsync();

        await viewModel.NavigateToDateAsync(targetDate);

        Assert.Equal(new DateTime(targetDate.Year, targetDate.Month, 1), viewModel.CurrentMonth);
        Assert.Equal(targetDate.Date, viewModel.SelectedDay?.Date);
        Assert.NotEqual(DateTime.Today, viewModel.SelectedDay?.Date);
    }

    [Fact]
    public async Task CalendarDay_UsesTagBackgroundWithoutChangingEventLabelColor()
    {
        var calendarEvent = CreateEvent("Important #important", DateTime.Today);
        calendarEvent.ColorId = "2";
        var viewModel = await CreateViewModelAsync([calendarEvent]);

        var day = viewModel.CalendarDays.Single(item => item.Date == DateTime.Today);
        var item = Assert.Single(day.Events);

        Assert.Equal("#FDE68A", day.TagBackgroundColor);
        Assert.Equal("#7AE7BF", item.DisplayColor);
    }

    [Fact]
    public async Task InitializeAsync_AppliesDisplaySettingsAndMondayStart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveSettingsAsync(new AppSettings
        {
            StartupCalendarViewMode = CalendarViewMode.Week,
            StartupTodoTabIndex = 1,
            CalendarLabelFontSizeIndex = 3,
            SideListFontSizeIndex = 1,
            WeekdayDisplayType = WeekdayDisplayType.JapaneseShort,
            WeekStartsOnMonday = true,
            WindowOpacity = 128
        });
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsWeekView);
        Assert.Equal(1, viewModel.SelectedTodoTabIndex);
        Assert.Equal(12, viewModel.CalendarLabelFontSize);
        Assert.Equal(11, viewModel.SideListFontSize);
        Assert.Equal("月", viewModel.WeekdayHeaders[0]);
        Assert.Equal(DayOfWeek.Monday, viewModel.CalendarDays[0].Date.DayOfWeek);
        Assert.Equal(128 / 255.0, viewModel.WindowOpacity);
    }

    private static async Task<MainViewModel> CreateViewModelAsync()
    {
        return await CreateViewModelAsync([]);
    }

    private static async Task<MainViewModel> CreateViewModelAsync(IEnumerable<CalendarEvent> events)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        foreach (var calendarEvent in events)
        {
            await repository.SaveEventAsync(calendarEvent);
        }

        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        viewModel.CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        await Task.Delay(50);
        return viewModel;
    }

    private static CalendarEvent CreateEvent(string title, DateTime date)
    {
        return new CalendarEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Start = new DateTimeOffset(date.Date.AddHours(9)),
            End = new DateTimeOffset(date.Date.AddHours(10))
        };
    }
}
