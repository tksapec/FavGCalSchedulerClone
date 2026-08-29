namespace FavGCalSchedulerClone.Tests;

public sealed class MonthEventLayerDragStateRegressionTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", ".."));

    [Fact]
    public async Task PreviewMouseLeftButtonDown_ClearsPreviousDragGestureBeforeHitTestingNewPress()
    {
        var path = Path.Combine(Root, "FavGCalSchedulerClone.App", "MainWindow.MonthEventLayer.cs");
        var source = await File.ReadAllTextAsync(path);
        var methodStart = source.IndexOf(
            "private async void MonthEventLayer_PreviewMouseLeftButtonDown",
            StringComparison.Ordinal);
        var nextMethod = source.IndexOf(
            "private void MonthEventLayer_PreviewMouseMove",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && nextMethod > methodStart);

        var method = source[methodStart..nextMethod];
        var clearPointIndex = method.IndexOf("_dragStartPoint = null;", StringComparison.Ordinal);
        var clearSegmentIndex = method.IndexOf("_dragSegment = null;", StringComparison.Ordinal);
        var hitTestGuardIndex = method.IndexOf("if (sender is not MonthEventLayer layer", StringComparison.Ordinal);

        Assert.True(clearPointIndex >= 0 && clearPointIndex < hitTestGuardIndex);
        Assert.True(clearSegmentIndex >= 0 && clearSegmentIndex < hitTestGuardIndex);
    }
}