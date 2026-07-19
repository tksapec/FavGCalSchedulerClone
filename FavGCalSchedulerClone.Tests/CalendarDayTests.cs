using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarDayTests
{
    [Fact]
    public void HiddenEventCount_ExposesPlusCountOnlyWhenEventsAreHidden()
    {
        var day = new CalendarDay();

        Assert.False(day.HasHiddenEvents);
        Assert.Equal(string.Empty, day.HiddenEventText);

        day.HiddenEventCount = 12;

        Assert.True(day.HasHiddenEvents);
        Assert.Equal("+12件", day.HiddenEventText);
    }

    [Fact]
    public async Task MonthCalendar_UsesOverlayChromeWithoutReducingSegmentArea()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "FavGCalSchedulerClone.App",
            "MainWindow.xaml"));
        var source = await File.ReadAllTextAsync(sourcePath);

        Assert.Contains("x:Name=\"MonthDayCellLayout\"", source);
        Assert.Contains("<ItemsControl ItemsSource=\"{Binding Segments}\"", source);
        Assert.DoesNotContain("<Grid x:Name=\"MonthDayCellLayout\">\r\n                                                <Grid.RowDefinitions>", source);
        Assert.Contains("Panel.ZIndex=\"10\"", source);
        Assert.Contains("Panel.ZIndex=\"20\"", source);
        Assert.Contains("Panel.ZIndex=\"30\"", source);
        Assert.Contains("Padding\" Value=\"26,0,2,0\"", source);

        var dayCellStyleStart = source.IndexOf("<Style x:Key=\"DayCell\"", StringComparison.Ordinal);
        var dayCellStyleEnd = source.IndexOf("</Style>", dayCellStyleStart, StringComparison.Ordinal);
        Assert.True(dayCellStyleStart >= 0 && dayCellStyleEnd > dayCellStyleStart);
        Assert.DoesNotContain("BorderThickness\" Value=\"3\"", source[dayCellStyleStart..dayCellStyleEnd]);
    }
}
