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
    public void GetTodoMetadata_AcceptsLegacyMaximumPriorityAndIntermediateProgress()
    {
        var item = new CalendarEvent { Description = "#todoF90%" };

        var metadata = TagService.GetTodoMetadata(item);

        Assert.NotNull(metadata);
        Assert.Equal("F", metadata.Priority);
        Assert.Equal(90, metadata.Progress);
        Assert.False(metadata.IsDone);
    }

    [Fact]
    public void UpdateTodoMarker_ReplacesExistingMarkerWithoutDuplicating()
    {
        var updated = TagService.UpdateTodoMarker("Existing body #todoB10% details", "A", 100);

        Assert.Equal($"#todoA100%{Environment.NewLine}Existing body details", updated);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(updated, "#todo", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    [Fact]
    public void UpdateTodoMarker_PreservesLinesAndEmptyLines()
    {
        var updated = TagService.UpdateTodoMarker("line 1\r\n\r\nline 2 #todoC20%", "F", 56);

        Assert.Equal($"#todoF56%{Environment.NewLine}line 1{Environment.NewLine}{Environment.NewLine}line 2", updated);
    }

    [Fact]
    public void GetTodoBodyForEditing_RemovesMarkerWithoutFlatteningLines()
    {
        var body = TagService.GetTodoBodyForEditing($"#todoF56%{Environment.NewLine}line 1{Environment.NewLine}{Environment.NewLine}line 2");

        Assert.Equal($"line 1{Environment.NewLine}{Environment.NewLine}line 2", body);
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

    [Fact]
    public void ResolveDisplayColor_UsesImportedEventColorWhenNoDisplayTagExists()
    {
        var item = new CalendarEvent { Title = "Imported", ColorId = "9" };

        var color = TagService.ResolveDisplayColors(item);

        Assert.Equal("#5484ED", color.Background);
        Assert.Equal("#FFFFFF", color.Foreground);
    }

    [Fact]
    public void ResolveDisplayColor_PrefersEventColorOverConfiguredTagColor()
    {
        var item = new CalendarEvent { Title = "Important #important", ColorId = "9" };

        var color = TagService.ResolveDisplayColors(item);

        Assert.Equal("#5484ED", color.Background);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("99")]
    public void ResolveDisplayColor_UsesWhiteLabelForMissingOrUnknownEventColor(string? colorId)
    {
        var item = new CalendarEvent { Title = "No color", ColorId = colorId };

        var color = TagService.ResolveDisplayColors(item);

        Assert.Equal("#FFFFFF", color.Background);
        Assert.Equal(TagService.DefaultDisplayForegroundColor, color.Foreground);
    }

    [Fact]
    public void ResolveDisplayColor_DoesNotUseHolidayOrWorkdayTagsForEventLabels()
    {
        var item = new CalendarEvent { Title = "Special #holiday #workday", ColorId = "9" };

        var color = TagService.ResolveDisplayColors(item);

        Assert.Equal("#5484ED", color.Background);
    }

    [Fact]
    public void ResolveDisplayColor_DoesNotUseDayBackgroundTagsForWhiteLabel()
    {
        var item = new CalendarEvent { Title = "Tagged #important #work #private #holiday" };

        var color = TagService.ResolveDisplayColors(item);

        Assert.Equal("#FFFFFF", color.Background);
    }

    [Fact]
    public void ResolveDayBackgroundColor_UsesHighestPriorityMatchingTag()
    {
        var date = new DateTime(2026, 5, 18);
        var work = AllDayEvent(date, "Work #work");
        var important = AllDayEvent(date, "Important #important");

        var color = TagService.ResolveDayBackgroundColor([work, important], date, TagService.DefaultTags);

        Assert.Equal("#FDE68A", color);
    }

    [Fact]
    public void ResolveDayBackgroundColor_WorkdayClearsHolidayOrDisplayTagBackground()
    {
        var date = new DateTime(2026, 5, 16);
        var special = AllDayEvent(date, "Saturday #holiday #important");
        var workday = AllDayEvent(date, "Workday override #workday");

        var color = TagService.ResolveDayBackgroundColor([special, workday], date, TagService.DefaultTags);

        Assert.Null(color);
    }

    [Fact]
    public void ResolveDisplayColor_UsesCachedGooglePaletteBeforeBuiltInFallback()
    {
        var item = new CalendarEvent { Title = "Remote", ColorId = "5" };
        var cached = new Dictionary<string, EventDisplayColors>
        {
            ["5"] = new("#123456", "#FEDCBA")
        };

        var color = TagService.ResolveDisplayColors(item, cached);

        Assert.Equal("#123456", color.Background);
        Assert.Equal("#FEDCBA", color.Foreground);
    }

    [Fact]
    public void ResolveSelectedDisplayColors_UsesVisibleTintForWhiteLabel()
    {
        var color = TagService.ResolveSelectedDisplayColors("#FFFFFF", "#111827");

        Assert.Equal("#DBEAFE", color.Background);
        Assert.Equal("#1E3A8A", color.Foreground);
    }

    [Theory]
    [InlineData("#A4BDFC", "#1D1D1D")]
    [InlineData("#7AE7BF", "#1D1D1D")]
    [InlineData("#5484ED", "#FFFFFF")]
    public void ResolveSelectedDisplayColors_DarkensColoredLabels(string background, string foreground)
    {
        var color = TagService.ResolveSelectedDisplayColors(background, foreground);

        Assert.NotEqual(background, color.Background);
        Assert.False(string.IsNullOrWhiteSpace(color.Foreground));
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
