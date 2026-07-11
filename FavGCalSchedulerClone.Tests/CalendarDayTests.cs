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
    public async Task MonthCalendar_KeepsHiddenEventFooterInItsOwnBottomRow()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "FavGCalSchedulerClone.App",
            "MainWindow.xaml"));
        var source = await File.ReadAllTextAsync(sourcePath);

        Assert.Contains("x:Name=\"MonthDayCellLayout\"", source);
        Assert.Contains("<ItemsControl Grid.Row=\"1\" ItemsSource=\"{Binding Segments}\"", source);
        Assert.Contains("Grid.Row=\"2\" Text=\"{Binding HiddenEventText}\"", source);
    }
}
