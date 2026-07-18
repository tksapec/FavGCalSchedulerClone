using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarWeekNumberTests
{
    [Fact]
    public void CreateRows_UsesIsoYearAtNewYearBoundary()
    {
        var rows = CalendarWeekNumber.CreateRows(new DateTime(2025, 12, 29), weekStartsOnMonday: true);

        var first = rows[0];
        Assert.Equal("W01", first.DisplayText);
        Assert.Equal("2026年第1週", first.ToolTipText);
    }

    [Fact]
    public void CreateRows_UsesMondayWhenGridStartsOnSunday()
    {
        var rows = CalendarWeekNumber.CreateRows(new DateTime(2025, 12, 28), weekStartsOnMonday: false);

        var first = rows[0];
        Assert.Equal(new DateTime(2025, 12, 29), first.IsoReferenceDate);
        Assert.Equal("W01", first.DisplayText);
    }

    [Fact]
    public void CreateRows_CreatesOneRowForEachMonthGridWeek()
    {
        var rows = CalendarWeekNumber.CreateRows(new DateTime(2026, 1, 4), weekStartsOnMonday: false);

        Assert.Equal(6, rows.Count);
    }
}
