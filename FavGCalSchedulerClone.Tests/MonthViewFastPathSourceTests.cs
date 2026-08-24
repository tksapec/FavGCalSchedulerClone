namespace FavGCalSchedulerClone.Tests;

public sealed class MonthViewFastPathSourceTests
{
    private static readonly string AppDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App"));

    private static readonly string MainWindowXamlPath = Path.Combine(AppDirectory, "MainWindow.xaml");
    private static readonly string FastPathPath = Path.Combine(AppDirectory, "MainWindow.MonthViewFastPath.cs");
    private static readonly string HandlerPath = Path.Combine(AppDirectory, "MainWindow.MonthEventLayer.cs");
    private static readonly string LayerPath = Path.Combine(AppDirectory, "Controls", "MonthEventLayer.cs");

    [Fact]
    public async Task MonthView_InstallsLightweightTemplateWhenTheMainViewModelIsAssigned()
    {
        var source = await File.ReadAllTextAsync(FastPathPath);

        Assert.Contains("protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)", source);
        Assert.Contains("e.Property != DataContextProperty", source);
        Assert.Contains("e.NewValue is not MainViewModel", source);
        Assert.Contains("DayList.ItemTemplate = CreateFastMonthDayTemplate();", source);
        Assert.Contains("new FrameworkElementFactory(typeof(MonthEventLayer))", source);
    }

    [Fact]
    public async Task MonthViewFastTemplate_DoesNotExpandSegmentsIntoAnItemsControl()
    {
        var source = await File.ReadAllTextAsync(FastPathPath);

        Assert.DoesNotContain("new FrameworkElementFactory(typeof(ItemsControl))", source);
        Assert.Contains("MonthEventLayer.SegmentsProperty", source);
        Assert.Contains("MonthEventLayer.EventFontSizeProperty", source);
    }

    [Fact]
    public async Task MonthViewFastTemplate_PreservesDayChromeAndInteractionHandlers()
    {
        var source = await File.ReadAllTextAsync(FastPathPath);

        Assert.Contains("SetResourceReference(FrameworkElement.StyleProperty, \"DayCell\")", source);
        Assert.Contains("CreateDateBadgeFactory()", source);
        Assert.Contains("CreateHiddenEventBadgeFactory()", source);
        Assert.Contains("CreateSelectionOverlayFactory()", source);
        Assert.Contains("DayCell_MouseLeftButtonDown", source);
        Assert.Contains("DayCell_MouseRightButtonDown", source);
        Assert.Contains("DayCell_DragOver", source);
        Assert.Contains("DayCell_Drop", source);
        Assert.Contains("MonthEventLayer_PreviewMouseLeftButtonDown", source);
        Assert.Contains("MonthEventLayer_MouseRightButtonDown", source);
    }

    [Fact]
    public async Task MonthView_ReplacesTheOriginalDayListDoubleClickHandlerWithMonthAwareHandling()
    {
        var fastPath = await File.ReadAllTextAsync(FastPathPath);
        var handlers = await File.ReadAllTextAsync(HandlerPath);

        Assert.Contains("DayList.MouseDoubleClick -= DayList_MouseDoubleClick", fastPath);
        Assert.Contains("DayList.MouseDoubleClick += MonthDayList_MouseDoubleClick", fastPath);
        Assert.Contains("FindMonthEventLayer(e.OriginalSource)", handlers);
        Assert.Contains("layer.HitTestSegment(e.GetPosition(layer))", handlers);
        Assert.Contains("e.Handled = true", handlers);
        Assert.Contains("await ShowScheduleDialogAsync()", handlers);
    }

    [Fact]
    public async Task WeekView_KeepsExistingPerSegmentControls()
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
}
