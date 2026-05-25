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
    public async Task SelectEventSegment_SelectsContinuationDateAndOriginalEvent()
    {
        var start = DateTime.Today.AddDays(1);
        var calendarEvent = new CalendarEvent
        {
            Id = "multi",
            Title = "Multi day",
            IsAllDay = true,
            Start = new DateTimeOffset(start),
            End = new DateTimeOffset(start.AddDays(3))
        };
        var viewModel = await CreateViewModelAsync([calendarEvent]);
        var continuationDate = start.AddDays(1).Date;
        var segment = viewModel.CalendarDays.Single(day => day.Date == continuationDate)
            .Segments.Single(item => item.Event?.Id == calendarEvent.Id);

        viewModel.SelectEventSegment(segment);

        Assert.Equal(calendarEvent.Id, viewModel.SelectedEvent?.Id);
        Assert.Equal(continuationDate, viewModel.SelectedDay?.Date);
        Assert.Contains(viewModel.SelectedDayEvents, item => item.Id == calendarEvent.Id);
    }

    [Fact]
    public async Task SelectEventSegment_HighlightsOnlyConnectedSegmentsInSelectedWeek()
    {
        var start = new DateTime(2026, 5, 15);
        var calendarEvent = new CalendarEvent
        {
            Id = "week-crossing",
            Title = "Week crossing",
            IsAllDay = true,
            Start = new DateTimeOffset(start),
            End = new DateTimeOffset(new DateTime(2026, 5, 20))
        };
        var viewModel = await CreateViewModelAsync([calendarEvent]);
        await viewModel.NavigateToDateAsync(start);
        var clickedSegment = viewModel.CalendarDays.Single(day => day.Date == new DateTime(2026, 5, 16))
            .Segments.Single(item => item.Event?.Id == calendarEvent.Id);

        viewModel.SelectEventSegment(clickedSegment);

        var selectedDates = viewModel.CalendarDays.SelectMany(day => day.Segments)
            .Where(segment => segment.IsSelected)
            .Select(segment => segment.Date)
            .ToArray();
        Assert.Equal([new DateTime(2026, 5, 15), new DateTime(2026, 5, 16)], selectedDates);
        Assert.All(
            viewModel.CalendarDays.SelectMany(day => day.Segments).Where(segment => segment.IsSelected),
            segment => Assert.NotEqual(segment.Event?.DisplayColor, segment.DisplayColor));
    }

    [Fact]
    public async Task SelectedDayChange_ClearsSegmentHighlightWhenSelectionLeavesEvent()
    {
        var eventDate = new DateTime(2026, 5, 11);
        var calendarEvent = CreateEvent("Selected label", eventDate);
        var viewModel = await CreateViewModelAsync([calendarEvent]);
        await viewModel.NavigateToDateAsync(eventDate);
        var segment = viewModel.CalendarDays.Single(day => day.Date == eventDate)
            .Segments.Single(item => item.Event?.Id == calendarEvent.Id);
        viewModel.SelectEventSegment(segment);

        viewModel.SelectedDay = viewModel.CalendarDays.Single(day => day.Date == eventDate.AddDays(1));

        Assert.DoesNotContain(viewModel.CalendarDays.SelectMany(day => day.Segments), item => item.IsSelected);
    }

    [Fact]
    public async Task MoveEventAsync_ShiftsTimedEventWithoutChangingDurationOrProperties()
    {
        var start = new DateTime(2026, 5, 11);
        var calendarEvent = CreateEvent("Move me", start);
        calendarEvent.ColorId = "2";
        calendarEvent.ReminderMinutesBeforeStart = 15;
        var viewModel = await CreateViewModelAsync([calendarEvent]);
        await viewModel.NavigateToDateAsync(start);
        var visibleEvent = viewModel.CalendarDays.Single(day => day.Date == start)
            .Segments.Single(item => item.Event?.Id == calendarEvent.Id).Event!;

        var moved = await viewModel.MoveEventAsync(visibleEvent, start, start.AddDays(3));

        Assert.True(moved);
        Assert.Equal(start.AddDays(3).AddHours(9), viewModel.SelectedEvent?.Start.DateTime);
        Assert.Equal(start.AddDays(3).AddHours(10), viewModel.SelectedEvent?.End.DateTime);
        Assert.Equal("2", viewModel.SelectedEvent?.ColorId);
        Assert.Equal(15, viewModel.SelectedEvent?.ReminderMinutesBeforeStart);
        Assert.Equal(start.AddDays(3), viewModel.SelectedDay?.Date);
    }

    [Fact]
    public async Task MoveEventAsync_UsesDraggedContinuationDayAsMultiDayAnchor()
    {
        var calendarEvent = new CalendarEvent
        {
            Id = "multi-move",
            Title = "Trip",
            IsAllDay = true,
            Start = new DateTimeOffset(new DateTime(2026, 5, 11)),
            End = new DateTimeOffset(new DateTime(2026, 5, 14))
        };
        var viewModel = await CreateViewModelAsync([calendarEvent]);
        await viewModel.NavigateToDateAsync(new DateTime(2026, 5, 11));
        var continuation = viewModel.CalendarDays.Single(day => day.Date == new DateTime(2026, 5, 12))
            .Segments.Single(item => item.Event?.Id == calendarEvent.Id).Event!;

        await viewModel.MoveEventAsync(continuation, new DateTime(2026, 5, 12), new DateTime(2026, 5, 20));

        Assert.Equal(new DateTime(2026, 5, 19), viewModel.SelectedEvent?.Start.Date);
        Assert.Equal(new DateTime(2026, 5, 22), viewModel.SelectedEvent?.End.Date);
        Assert.Equal(new DateTime(2026, 5, 20), viewModel.SelectedDay?.Date);
    }

    [Fact]
    public async Task MoveEventAsync_RequiresScopeForRecurringEvent()
    {
        var start = new DateTime(2026, 5, 11);
        var recurring = CreateEvent("Recurring", start);
        recurring.RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=2\"]";
        var viewModel = await CreateViewModelAsync([recurring]);
        await viewModel.NavigateToDateAsync(start);

        var moved = await viewModel.MoveEventAsync(recurring, start, start.AddDays(1));

        Assert.False(moved);
        Assert.Equal(start, recurring.Start.Date);
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
    public async Task CalendarStatusText_UsesSelectedDayAndOriginalWeekNumbering()
    {
        var viewModel = await CreateViewModelAsync();

        await viewModel.NavigateToDateAsync(new DateTime(2026, 5, 24));

        Assert.Contains("2026", viewModel.CalendarStatusText);
        Assert.Contains("05月24日", viewModel.CalendarStatusText);
        Assert.Contains("第4日曜日", viewModel.CalendarStatusText);
        Assert.Contains("21週目", viewModel.CalendarStatusText);
        Assert.Contains("経過日数 144日", viewModel.CalendarStatusText);
    }

    [Fact]
    public async Task CalendarDay_RetiredDayTagRemainsVisibleWithoutChangingEventLabelColor()
    {
        var calendarEvent = CreateEvent("Visible event", DateTime.Today);
        calendarEvent.ColorId = "2";
        var ordinaryEvent = CreateEvent("Ordinary tag text", DateTime.Today);
        ordinaryEvent.Description = "#important";
        var viewModel = await CreateViewModelAsync([calendarEvent, ordinaryEvent]);

        var day = viewModel.CalendarDays.Single(item => item.Date == DateTime.Today);
        var item = day.Events.Single(item => item.Title == calendarEvent.Title);

        Assert.Equal("#7AE7BF", item.DisplayColor);
        Assert.Contains(day.Segments, segment => segment.Event?.Title == ordinaryEvent.Title && segment.IsVisible);
    }

    [Fact]
    public async Task CalendarDay_HolidayDirectiveIsHiddenAndUsesSundayBackgroundState()
    {
        var date = new DateTime(2026, 5, 18);
        var directive = CreateEvent("Holiday directive", date);
        directive.Description = "#holiday";
        var viewModel = await CreateViewModelAsync([directive]);
        await viewModel.NavigateToDateAsync(date);

        var day = viewModel.CalendarDays.Single(item => item.Date == date);
        Assert.True(day.IsHoliday);
        Assert.Empty(day.Events);
        Assert.DoesNotContain(day.Segments, segment => segment.IsVisible);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    public async Task CalendarDay_WorkdayDirectiveOnWeekendIsHiddenAndUsesWeekdayState(int dayNumber)
    {
        var date = new DateTime(2026, 5, dayNumber);
        var directive = CreateEvent("Workday directive", date);
        directive.Description = "#workday";
        var viewModel = await CreateViewModelAsync([directive]);
        await viewModel.NavigateToDateAsync(date);

        var day = viewModel.CalendarDays.Single(item => item.Date == date);
        Assert.True(day.IsWeekend);
        Assert.True(day.IsWorkdayOverride);
        Assert.False(day.IsHoliday);
        Assert.Empty(day.Events);
        Assert.DoesNotContain(day.Segments, segment => segment.IsVisible);
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
