using System.Windows;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class WindowPlacementServiceTests
{
    [Fact]
    public void Normalize_MovesAnOffScreenWindowToTheAvailableWorkArea()
    {
        var result = WindowPlacementService.Normalize(
            new WindowPlacement(-5000, -3000, 1180, 760, false),
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
            new WindowPlacement(-1800, 40, 1180, 760, true),
            1040,
            720,
            [new Rect(0, 0, 1920, 1080), new Rect(-1920, 0, 1920, 1080)]);

        Assert.Equal(-1800, result.Left);
        Assert.True(result.IsMaximized);
    }
}
