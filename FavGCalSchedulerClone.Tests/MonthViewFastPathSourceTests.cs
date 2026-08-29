namespace FavGCalSchedulerClone.Tests;

public sealed class MonthViewFastPathSourceTests
{
    private static readonly string AppDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App"));

    private static readonly string MainWindowXamlPath = Path.Combine(AppDirectory, "MainWindow.xaml");
    private static readonly string LegacyFastPathPath = Path.Combine(AppDirectory, "MainWindow.MonthViewFastPath.cs");
    private static readonly string HandlerPath = Path.Combine(AppDirectory, "MainWindow.MonthEventLayer.cs");
    private static readonly string LayerPath = Path.Combine(AppDirectory, "Controls", "MonthEventLayer.cs");

    [Fact]
    public async Task MonthView_DefinesTheLightweightRendererDirectlyInXaml()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);
        var monthRegion = GetMonthRegion(xaml);

        Assert.Contains("xmlns:controls=\"clr-namespace:FavGCalSchedulerClone.App.Controls\"", xaml);
        Assert.Contains("<controls:MonthEventLayer Segments=\"{Binding Segments}\"", monthRegion);
        Assert.Contains("EventFontSize=\"{Binding DataContext.CalendarLabelFontSize", monthRegion);
        Assert.DoesNotContain("<ItemsControl ItemsSource=\"{Binding Segments}\"", monthRegion);
        Assert.False(File.Exists(LegacyFastPathPath), "The deprecated runtime FrameworkElementFactory template must not be restored.");
    }

    [Fact]
    public async Task MonthView_PreservesDayChromeAndInteractionHandlersInXaml()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);
        var monthRegion = GetMonthRegion(xaml);

        Assert.Contains("Style=\"{StaticResource DayCell}\"", monthRegion);
        Assert.Contains("ToolTip=\"{Binding DayToolTipText}\"", monthRegion);
        Assert.Contains("DayCell_MouseLeftButtonDown", monthRegion);
        Assert.Contains("DayCell_MouseRightButtonDown", monthRegion);
        Assert.Contains("DayCell_DragOver", monthRegion);
        Assert.Contains("DayCell_DragLeave", monthRegion);
        Assert.Contains("DayCell_Drop", monthRegion);
        Assert.Contains("MonthEventLayer_PreviewMouseLeftButtonDown", monthRegion);
        Assert.Contains("MonthEventLayer_PreviewMouseMove", monthRegion);
        Assert.Contains("MonthEventLayer_MouseLeftButtonDown", monthRegion);
        Assert.Contains("MonthEventLayer_MouseRightButtonDown", monthRegion);
        Assert.Contains("Panel.ZIndex=\"10\"", monthRegion);
        Assert.Contains("Panel.ZIndex=\"20\"", monthRegion);
        Assert.Contains("Panel.ZIndex=\"30\"", monthRegion);
    }

    [Fact]
    public async Task MonthView_UsesMonthAwareDoubleClickDirectlyWithoutRuntimeHandlerSwapping()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);
        var monthRegion = GetMonthRegion(xaml);
        var handlers = await File.ReadAllTextAsync(HandlerPath);

        Assert.Contains("MouseDoubleClick=\"MonthDayList_MouseDoubleClick\"", monthRegion);
        Assert.Contains("FindMonthEventLayer(e.OriginalSource)", handlers);
        Assert.Contains("layer.HitTestSegment(e.GetPosition(layer))", handlers);
        Assert.Contains("e.Handled = true", handlers);
        Assert.Contains("await ShowScheduleDialogAsync()", handlers);
    }

    [Fact]
    public async Task WeekView_KeepsExistingPerSegmentControlsAndDoubleClickHandler()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);

        var weekViewStart = xaml.IndexOf(
            "Visibility=\"{Binding IsWeekView, Converter={StaticResource BoolToVisibilityConverter}}\"",
            StringComparison.Ordinal);
        Assert.True(weekViewStart >= 0, "Week view was not found.");

        var dayViewStart = xaml.IndexOf(
            "Visibility=\"{Binding IsDayView, Converter={StaticResource BoolToVisibilityConverter}}\"",
            weekViewStart,
            StringComparison.Ordinal);
        Assert.True(dayViewStart > weekViewStart, "Day view was not found.");

        var weekRegion = xaml[weekViewStart..dayViewStart];
        Assert.Contains("<ItemsControl ItemsSource=\"{Binding Segments}\"", weekRegion);
        Assert.Contains("EventSegment_PreviewMouseLeftButtonDown", weekRegion);
        Assert.Contains("EventSegment_MouseRightButtonDown", weekRegion);
        Assert.Contains("MouseDoubleClick=\"DayList_MouseDoubleClick\"", weekRegion);
        Assert.DoesNotContain("controls:MonthEventLayer", weekRegion);
    }

    [Fact]
    public async Task MonthEventLayer_DrawsDirectlyAndInvalidatesOnSegmentChanges()
    {
        Assert.True(File.Exists(LayerPath), "MonthEventLayer.cs was not found.");
        var source = await File.ReadAllTextAsync(LayerPath);

        Assert.Contains("sealed class MonthEventLayer : FrameworkElement", source);
        Assert.Contains("protected override void OnRender(DrawingContext drawingContext)", source);
        Assert.Contains("public CalendarEventSegment? HitTestSegment(Point point)", source);
        Assert.Contains("drawingContext.DrawRoundedRectangle(", source);
        Assert.Contains("FormattedText(", source);
        Assert.Contains("CollectionChanged += Segments_CollectionChanged", source);
        Assert.Contains("segment.PropertyChanged += Segment_PropertyChanged", source);
        Assert.DoesNotContain("ItemsControl", source);
        Assert.DoesNotContain("new Border", source);
        Assert.DoesNotContain("new TextBlock", source);
    }

    [Fact]
    public async Task MonthEventLayerHandlers_ReuseExistingSelectionDragAndContextMenuFlow()
    {
        var source = await File.ReadAllTextAsync(HandlerPath);

        Assert.Contains("layer.HitTestSegment(e.GetPosition(layer))", source);
        Assert.Contains("_viewModel.SelectEventSegment(segment)", source);
        Assert.Contains("DragDrop.DoDragDrop(layer, segment, DragDropEffects.Move)", source);
        Assert.Contains("ShowCalendarContextMenu(layer)", source);
        Assert.Contains("await OpenSelectedEventEditorAsync()", source);
        Assert.Contains("ReferenceEquals(layer.HitTestSegment(e.GetPosition(layer)), segment)", source);
    }

    private static string GetMonthRegion(string xaml)
    {
        var monthViewStart = xaml.IndexOf(
            "Visibility=\"{Binding IsMonthView, Converter={StaticResource BoolToVisibilityConverter}}\"",
            StringComparison.Ordinal);
        Assert.True(monthViewStart >= 0, "Month view was not found.");

        var weekViewStart = xaml.IndexOf(
            "Visibility=\"{Binding IsWeekView, Converter={StaticResource BoolToVisibilityConverter}}\"",
            monthViewStart,
            StringComparison.Ordinal);
        Assert.True(weekViewStart > monthViewStart, "Week view was not found after month view.");
        return xaml[monthViewStart..weekViewStart];
    }
}
