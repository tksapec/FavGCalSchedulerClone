using System.Windows;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class WindowPlacementServiceTests
{
    [Fact]
    public void Normalize_MovesAnOffScreenWindowToTheAvailableWorkArea()
    {
        var result = WindowPlacementService.Normalize(
            new WindowPlacement(Left: -5000, Top: -3000, Width: 1180, Height: 760, IsMaximized: false),
            1040,
            720,
            [new Rect(0, 0, 1920, 1080)]);

        Assert.True(result.Left >= 0);
        Assert.True(result.Top >= 0);
        Assert.Equal(1180, result.Width);
        Assert.Equal(760, result.Height);
    }

    [Fact]
    public void Normalize_PreservesAVisibleSecondaryMonitorPlacement()
    {
        var result = WindowPlacementService.Normalize(
            new WindowPlacement(Left: -1800, Top: 40, Width: 1180, Height: 760, IsMaximized: true),
            1040,
            720,
            [new Rect(0, 0, 1920, 1080), new Rect(-1920, 0, 1920, 1080)]);

        Assert.Equal(-1800, result.Left);
        Assert.True(result.IsMaximized);
    }

    [Fact]
    public void Normalize_ShrinksOnlyWhenWindowExceedsWorkArea()
    {
        var normal = WindowPlacementService.Normalize(
            new WindowPlacement(Left: 10, Top: 10, Width: 1180, Height: 760, IsMaximized: false),
            960, 600, [new Rect(0, 0, 1920, 1080)]);
        var oversized = WindowPlacementService.Normalize(
            new WindowPlacement(Left: 10, Top: 10, Width: 2500, Height: 1400, IsMaximized: false),
            960, 600, [new Rect(0, 0, 1920, 1080)]);

        Assert.Equal(1180, normal.Width);
        Assert.Equal(760, normal.Height);
        Assert.Equal(1920, oversized.Width);
        Assert.Equal(1080, oversized.Height);
    }

    [Theory]
    [InlineData(0, 760)]
    [InlineData(1180, 0)]
    [InlineData(double.NaN, 760)]
    [InlineData(double.PositiveInfinity, 760)]
    public void Normalize_RepairsInvalidDimensions(double width, double height)
    {
        var result = WindowPlacementService.Normalize(
            new WindowPlacement(Left: 0, Top: 0, Width: width, Height: height, IsMaximized: false),
            960, 600, [new Rect(0, 0, 1920, 1080)]);

        Assert.True(result.Width >= 960);
        Assert.True(result.Height >= 600);
        Assert.True(double.IsFinite(result.Width));
        Assert.True(double.IsFinite(result.Height));
    }
}
