using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarUiServiceTests
{
    [Fact]
    public void CalendarStatusFormatter_FormatsSelectedDateStatusWithSimpleWeekNumber()
    {
        var text = CalendarStatusFormatter.FormatCalendarStatus(new DateTime(2026, 5, 24));

        Assert.Contains("2026年(令和8年)05月24日", text);
        Assert.Contains("21週目", text);
        Assert.Contains("経過日数 144日", text);
    }

    [Fact]
    public void CalendarStatusFormatter_CreatesMondayStartHeaders()
    {
        var headers = CalendarStatusFormatter.CreateWeekdayHeaders(WeekdayDisplayType.EnglishShort, weekStartsOnMonday: true);

        Assert.Equal(["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"], headers);
    }

    [Fact]
    public void CalendarVisibleRangeService_ReturnsWeekContainingAnchor()
    {
        var days = Enumerable.Range(1, 31)
            .Select(day => new CalendarDay { Date = new DateTime(2026, 5, day) })
            .ToArray();

        var visibleDates = CalendarVisibleRangeService
            .GetVisibleDates(CalendarViewMode.Week, days, new DateTime(2026, 5, 24), weekStartsOnMonday: false)
            .ToArray();

        Assert.Equal(new DateTime(2026, 5, 24), visibleDates[0]);
        Assert.Equal(new DateTime(2026, 5, 30), visibleDates[^1]);
    }

    [Fact]
    public void TodoDisplayFilter_ExcludesOldTasksOnlyWhenPeriodIsSpecified()
    {
        var today = new DateTime(2026, 5, 30);
        var oldTodo = new CalendarEvent { Start = new DateTimeOffset(today.AddMonths(-4)) };

        Assert.True(TodoDisplayFilter.IsWithinDisplayPeriod(oldTodo, months: 0, today));
        Assert.False(TodoDisplayFilter.IsWithinDisplayPeriod(oldTodo, months: 3, today));
    }

    [Fact]
    public void AppSettingsNormalizer_ClampsInvalidValuesAndSeedsVisibleCalendar()
    {
        var settings = AppSettingsNormalizer.Normalize(new AppSettings
        {
            ActiveCalendarId = "",
            StartupTabIndex = 99,
            StartupTodoTabIndex = 99,
            CalendarLabelFontSizeIndex = 99,
            SideListFontSizeIndex = 0,
            WindowOpacity = 1,
            ReminderSoundVolume = 999,
            IncompleteTodoDisplayPeriodMonths = 2,
            CompletedTodoDisplayPeriodMonths = 99,
            AutomaticSyncIntervalMinutes = 45,
            VisibleCalendarIds = ["", "primary", "primary"]
        });

        Assert.Equal(4, settings.StartupTabIndex);
        Assert.Equal(1, settings.StartupTodoTabIndex);
        Assert.Equal(3, settings.CalendarLabelFontSizeIndex);
        Assert.Equal(1, settings.SideListFontSizeIndex);
        Assert.Equal(64, settings.WindowOpacity);
        Assert.Equal(100, settings.ReminderSoundVolume);
        Assert.Equal(0, settings.IncompleteTodoDisplayPeriodMonths);
        Assert.Equal(0, settings.CompletedTodoDisplayPeriodMonths);
        Assert.Null(settings.AutomaticSyncIntervalMinutes);
        Assert.Equal(["primary"], settings.VisibleCalendarIds);
        Assert.Equal("primary", settings.ActiveCalendarId);
    }
}
