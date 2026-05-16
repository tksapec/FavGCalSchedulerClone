using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class TagServiceTests
{
    [Fact]
    public void ExtractTags_ReturnsDistinctTagsFromTitleAndDescription()
    {
        var tags = TagService.ExtractTags("会議 #work #important", "memo #work #holiday");

        Assert.Equal(["#holiday", "#important", "#work"], tags);
    }

    [Fact]
    public void IsHoliday_DetectsHolidayTagInDescription()
    {
        var item = new CalendarEvent { Title = "振替休日", Description = "Fav互換 #holiday" };

        Assert.True(TagService.IsHoliday(item));
    }

    [Fact]
    public void IsTodoLike_DetectsFavGCalTodoMarker()
    {
        var item = new CalendarEvent { Title = "確認", Description = "#todoA56% 進捗管理" };

        Assert.True(TagService.IsTodoLike(item));
    }

    [Fact]
    public void GetTodoMetadata_ParsesPriorityAndProgress()
    {
        var item = new CalendarEvent { Description = "本文 #todoA56%" };

        var metadata = TagService.GetTodoMetadata(item);

        Assert.NotNull(metadata);
        Assert.Equal("A", metadata.Priority);
        Assert.Equal(56, metadata.Progress);
        Assert.False(metadata.IsDone);
    }

    [Fact]
    public void GetTodoMetadata_TreatsOneHundredPercentAsDone()
    {
        var item = new CalendarEvent { Description = "#todo100%" };

        var metadata = TagService.GetTodoMetadata(item);

        Assert.NotNull(metadata);
        Assert.Equal("", metadata.Priority);
        Assert.Equal(100, metadata.Progress);
        Assert.True(metadata.IsDone);
    }

    [Fact]
    public void GetTodoMetadata_IgnoresCase()
    {
        var item = new CalendarEvent { Title = "#TODOB0%" };

        var metadata = TagService.GetTodoMetadata(item);

        Assert.NotNull(metadata);
        Assert.Equal("B", metadata.Priority);
        Assert.Equal(0, metadata.Progress);
    }

    [Fact]
    public void UpdateTodoMarker_ReplacesExistingMarkerWithoutDuplicating()
    {
        var updated = TagService.UpdateTodoMarker("既存本文 #todoB10% 詳細", "A", 100);

        Assert.Equal("#todoA100% 既存本文 詳細", updated);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(updated, "#todo", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    [Fact]
    public void IsWorkday_DetectsWorkdayTagInTitle()
    {
        var item = new CalendarEvent { Title = "休日出勤 #workday" };

        Assert.True(TagService.IsWorkday(item));
    }

    [Fact]
    public void IsWorkday_IgnoresCase()
    {
        var item = new CalendarEvent { Description = "振替出勤 #WORKDAY" };

        Assert.True(TagService.IsWorkday(item));
    }

    [Fact]
    public void WorkdayOverride_WinsOverHolidayOnSameEvent()
    {
        var date = new DateTime(2026, 5, 16);
        var item = AllDayEvent(date, "休日出勤 #holiday #workday");

        Assert.True(TagService.HasWorkdayOverride([item], date));
        Assert.False(TagService.HasHolidayWithoutWorkdayOverride([item], date));
    }

    [Fact]
    public void WorkdayOverride_WinsOverHolidayOnSeparateEvents()
    {
        var date = new DateTime(2026, 5, 17);
        var holiday = AllDayEvent(date, "休日 #holiday");
        var workday = AllDayEvent(date, "出勤日 #workday");

        Assert.True(TagService.HasWorkdayOverride([holiday, workday], date));
        Assert.False(TagService.HasHolidayWithoutWorkdayOverride([holiday, workday], date));
    }

    [Fact]
    public void WorkdayOverride_AppliesToWeekendDate()
    {
        var saturday = new DateTime(2026, 5, 16);
        var item = AllDayEvent(saturday, "土曜出勤 #workday");

        Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek);
        Assert.True(TagService.HasWorkdayOverride([item], saturday));
    }

    private static CalendarEvent AllDayEvent(DateTime date, string title)
    {
        return new CalendarEvent
        {
            Title = title,
            IsAllDay = true,
            Start = new DateTimeOffset(date.Date),
            End = new DateTimeOffset(date.Date.AddDays(1))
        };
    }
}
