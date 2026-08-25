using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class TagServiceTests
{
    [Fact]
    public void ExtractTags_UsesOnlyStandaloneDescriptionLines()
    {
        var tags = TagService.ExtractTags("#important", "memo #work\n #holiday \n#workday\n今日は#private\n#important\n#work\n#private");

        Assert.Equal(["#holiday", "#workday"], tags);
    }

    [Fact]
    public void IsHoliday_DetectsStandaloneHolidayLineInDescription()
    {
        var item = new CalendarEvent { Title = "振替休日 #workday", Description = "説明\n#holiday" };

        Assert.True(TagService.IsHoliday(item));
    }

    [Fact]
    public void IsHoliday_DoesNotDetectTagEmbeddedInDescriptionText()
    {
        var item = new CalendarEvent { Description = "今日は#holiday" };

        Assert.False(TagService.IsHoliday(item));
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
    public void UpdateTodoMarker_PreservesTabsAndInlineWhitespace()
    {
        var body = "line 1\tcolumn 2\r\n  indented line";

        var updated = TagService.UpdateTodoMarker(body, "A", 20);

        Assert.Equal($"#todoA20%{Environment.NewLine}{body}", updated);
        Assert.Equal(body, TagService.GetTodoBodyForEditing(updated));
    }

    [Fact]
    public void IsWorkday_DoesNotDetectWorkdayTagInTitle()
    {
        var item = new CalendarEvent { Title = "休日出勤 #workday" };

        Assert.False(TagService.IsWorkday(item));
    }

    [Fact]
    public void IsWorkday_IgnoresCase()
    {
        var item = new CalendarEvent { Description = "#WORKDAY" };

        Assert.True(TagService.IsWorkday(item));
    }

    [Fact]
    public void WorkdayOverride_WinsOverHolidayOnSameEvent()
    {
        var date = new DateTime(2026, 5, 16);
        var item = AllDayEvent(date, "休日出勤");
        item.Description = "#holiday\n#workday";

        Assert.True(TagService.HasWorkdayOverride([item], date));
        Assert.False(TagService.HasHolidayWithoutWorkdayOverride([item], date));
    }

    [Fact]
    public void WorkdayOverride_WinsOverHolidayOnSeparateEvents()
    {
        var date = new DateTime(2026, 5, 17);
        var holiday = AllDayEvent(date, "休日");
        holiday.Description = "#holiday";
        var workday = AllDayEvent(date, "出勤日");
        workday.Description = "#workday";

        Assert.True(TagService.HasWorkdayOverride([holiday, workday], date));
        Assert.False(TagService.HasHolidayWithoutWorkdayOverride([holiday, workday], date));
    }

    [Fact]
    public void WorkdayOverride_AppliesToWeekendDate()
    {
        var saturday = new DateTime(2026, 5, 16);
        var item = AllDayEvent(saturday, "土曜出勤");
        item.Description = "#workday";

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
    public void RetiredDayTags_ArePlainTextAndNotDirectives()
    {
        var item = AllDayEvent(new DateTime(2026, 5, 18), "Ordinary description");
        item.Description = "#important";

        Assert.False(TagService.IsDayCellDirective(item));
        Assert.False(TagService.IsHoliday(item));
        Assert.False(TagService.IsWorkday(item));
    }

    [Theory]
    [InlineData("#holiday")]
    [InlineData("#workday")]
    public void SupportedDayTag_IsADirective(string description)
    {
        var item = AllDayEvent(new DateTime(2026, 5, 16), "Directive");
        item.Description = description;

        Assert.True(TagService.IsDayCellDirective(item));
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
    public void ResolveDisplayColor_NullCachedPaletteValue_UsesBuiltInFallback()
    {
        var item = new CalendarEvent { Title = "Remote", ColorId = "5" };
        var cached = new Dictionary<string, EventDisplayColors>
        {
            ["5"] = null!
        };

        var color = TagService.ResolveDisplayColors(item, cached);

        Assert.Equal("#FBD75B", color.Background);
        Assert.Equal("#1D1D1D", color.Foreground);
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
