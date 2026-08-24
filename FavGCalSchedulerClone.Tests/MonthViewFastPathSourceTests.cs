namespace FavGCalSchedulerClone.Tests;

public sealed class MonthViewFastPathSourceTests
{
    private static readonly string AppDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App"));

    private static readonly string MainWindowXamlPath = Path.Combine(AppDirectory, "MainWindow.xaml");
    private static readonly string LayerPath = Path.Combine(AppDirectory, "Controls", "MonthEventLayer.cs");

    [Fact]
    public async Task MonthView_InstallsLightweightTemplateWhenTheMainViewModelIsAssigned()
    {
        var sources = await ReadMainWindowSourcesAsync();

        Assert.Contains("protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)", sources);
        Assert.Contains("e.Property != DataContextProperty", sources);
        Assert.Contains("e.NewValue is not MainViewModel", sources);
        Assert.Contains("DayList.ItemTemplate = CreateFastMonthDayTemplate();", sources);
        Assert.Contains("new FrameworkElementFactory(typeof(MonthEventLayer))", sources);
    }

    [Fact]
    public async Task MonthViewFastTemplate_DoesNotExpandSegmentsIntoAnItemsControl()
    {
        var sources = await ReadMainWindowSourcesAsync();

        Assert.DoesNotContain("new FrameworkElementFactory(typeof(ItemsControl))", sources);
        Assert.Contains("MonthEventLayer.SegmentsProperty", sources);
        Assert.Contains("MonthEventLayer.EventFontSizeProperty", sources);
    }

    [Fact]
    public async Task MonthViewFastTemplate_PreservesDayChromeAndInteractionHandlers()
    {
        var sources = await ReadMainWindowSourcesAsync();

        Assert.Contains("SetResourceReference(FrameworkElement.StyleProperty, \"DayCell\")", sources);
        Assert.Contains("CreateDateBadgeFactory()", sources);
        Assert.Contains("CreateHiddenEventBadgeFactory()", sources);
        Assert.Contains("CreateSelectionOverlayFactory()", sources);
        Assert.Contains("DayCell_MouseLeftButtonDown", sources);
        Assert.Contains("DayCell_MouseRightButtonDown", sources);
        Assert.Contains("DayCell_DragOver", sources);
        Assert.Contains("DayCell_Drop", sources);
        Assert.Contains("MonthEventLayer_PreviewMouseLeftButtonDown", sources);
        Assert.Contains("MonthEventLayer_MouseRightButtonDown", sources);
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
        var sources = await ReadMainWindowSourcesAsync();

        Assert.Contains("layer.HitTestSegment(e.GetPosition(layer))", sources);
        Assert.Contains("_viewModel.SelectEventSegment(segment)", sources);
        Assert.Contains("DragDrop.DoDragDrop(layer, segment, DragDropEffects.Move)", sources);
        Assert.Contains("ShowCalendarContextMenu(layer)", sources);
        Assert.Contains("await OpenSelectedEventEditorAsync()", sources);
    }

    private static async Task<string> ReadMainWindowSourcesAsync()
    {
        var paths = Directory.GetFiles(AppDirectory, "MainWindow*.cs", SearchOption.TopDirectoryOnly);
        var sources = new List<string>(paths.Length);
        foreach (var path in paths)
        {
            sources.Add(await File.ReadAllTextAsync(path));
        }

        return string.Join(Environment.NewLine, sources);
    }
}
