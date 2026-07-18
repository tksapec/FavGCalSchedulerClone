using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarWeekNumberTests
{
    [Fact]
    public void CreateRows_UsesTheIsoYearAtTheNewYearBoundary()
    {
        var rows = CalendarWeekNumber.CreateRows(new DateTime(2025, 12, 29), weekStartsOnMonday: true);

        Assert.Equal("01", rows[0].DisplayText);
        Assert.Equal("2026年第1週", rows[0].ToolTipText);
    }

    [Fact]
    public void CreateRows_UsesMondayForAnSundayStartGrid()
    {
        var rows = CalendarWeekNumber.CreateRows(new DateTime(2025, 12, 28), weekStartsOnMonday: false);

        Assert.Equal(new DateTime(2025, 12, 29), rows[0].IsoReferenceDate);
        Assert.Equal("01", rows[0].DisplayText);
    }

    [Fact]
    public void CreateRows_AlwaysCreatesTheSixMonthGridRows()
    {
        var rows = CalendarWeekNumber.CreateRows(new DateTime(2026, 1, 4), weekStartsOnMonday: false);

        Assert.Equal(6, rows.Count);
    }
}
