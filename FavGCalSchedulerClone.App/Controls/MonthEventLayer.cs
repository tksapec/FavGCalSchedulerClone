using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Controls;

/// <summary>
/// Renders the month-view event lanes without expanding every segment into a WPF control tree.
/// One instance is hosted by each month day cell.
/// </summary>
public sealed class MonthEventLayer : FrameworkElement
{
    internal const double LanePitch = 16;
    internal const double BarTopOffset = 1;
    internal const double BarHeight = 15;
    private const double CornerRadius = 2;
    private const double TodoIndicatorSize = 12;
    private const double TodoIndicatorGap = 3;

    private static readonly ConcurrentDictionary<string, Brush> BrushCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Typeface EventTypeface = new(
        new FontFamily("Meiryo UI"),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);

    private static readonly Typeface TodoTypeface = new(
        new FontFamily("Meiryo UI"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);

    private static readonly Pen NormalBorderPen = CreateFrozenPen("#64748B", 1);
    private static readonly Pen SelectedBorderPen = CreateFrozenPen("#1D4ED8", 2);
    private static readonly Pen TodoBorderPen = CreateFrozenPen("#475569", 1);
    private static readonly Brush TodoCheckBrush = ResolveBrush("#DC2626");

    private readonly HashSet<CalendarEventSegment> _observedSegments = [];
    private IList<CalendarEventSegment>? _subscribedSegments;
    private INotifyCollectionChanged? _subscribedCollection;
    private CalendarEventSegment? _toolTipSegment;

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments),
        typeof(IList<CalendarEventSegment>),
        typeof(MonthEventLayer),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.AffectsRender,
            OnSegmentsChanged));

    public static readonly DependencyProperty EventFontSizeProperty = DependencyProperty.Register(
        nameof(EventFontSize),
        typeof(double),
        typeof(MonthEventLayer),
        new FrameworkPropertyMetadata(
            12d,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public MonthEventLayer()
    {
        SnapsToDevicePixels = true;
        Loaded += MonthEventLayer_Loaded;
        Unloaded += MonthEventLayer_Unloaded;
    }

    public IList<CalendarEventSegment>? Segments
    {
        get => (IList<CalendarEventSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double EventFontSize
    {
        get => (double)GetValue(EventFontSizeProperty);
        set => SetValue(EventFontSizeProperty, value);
    }

    public CalendarEventSegment? HitTestSegment(Point point)
    {
        var segments = Segments;
        if (segments is null
            || point.X < 0
            || point.X >= RenderSize.Width
            || point.Y < 0
            || point.Y >= RenderSize.Height)
        {
            return null;
        }

        var lane = (int)(point.Y / LanePitch);
        if (lane < 0 || lane >= segments.Count)
        {
            return null;
        }

        var laneOffset = point.Y - lane * LanePitch;
        if (laneOffset < BarTopOffset || laneOffset >= BarTopOffset + BarHeight)
        {
            return null;
        }

        CalendarEventSegment? segment = segments[lane];
        if (segment.Lane != lane)
        {
            segment = segments.FirstOrDefault(candidate => candidate.Lane == lane);
        }

        return segment?.Event is null ? null : segment;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            return;
        }

        // Keep the whole cell hit-testable while avoiding child UIElements.
        drawingContext.DrawRectangle(
            Brushes.Transparent,
            null,
            new Rect(new Point(0, 0), RenderSize));

        var segments = Segments;
        if (segments is null || segments.Count == 0)
        {
            return;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var segment in segments)
        {
            if (!segment.IsVisible)
            {
                continue;
            }

            var top = segment.Lane * LanePitch + BarTopOffset;
            if (top >= RenderSize.Height)
            {
                continue;
            }

            DrawSegment(drawingContext, segment, top, pixelsPerDip);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var segment = HitTestSegment(e.GetPosition(this));
        if (ReferenceEquals(_toolTipSegment, segment))
        {
            return;
        }

        _toolTipSegment = segment;
        ToolTip = segment?.ToolTipText;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _toolTipSegment = null;
        ToolTip = null;
        base.OnMouseLeave(e);
    }

    private void DrawSegment(
        DrawingContext drawingContext,
        CalendarEventSegment segment,
        double top,
        double pixelsPerDip)
    {
        var clipHeight = Math.Min(BarHeight, Math.Max(0, RenderSize.Height - top));
        if (clipHeight <= 0)
        {
            return;
        }

        drawingContext.PushClip(new RectangleGeometry(
            new Rect(0, top, RenderSize.Width, clipHeight)));

        try
        {
            var pen = segment.IsSelected ? SelectedBorderPen : NormalBorderPen;
            var borderThickness = pen.Thickness;
            var left = segment.IsWeekSegmentStart ? 0 : -CornerRadius;
            var right = segment.IsWeekSegmentEnd ? RenderSize.Width : RenderSize.Width + CornerRadius;
            var radius = segment.IsWeekSegmentStart || segment.IsWeekSegmentEnd ? CornerRadius : 0;

            var shapeRect = new Rect(
                left + borderThickness / 2,
                top + borderThickness / 2,
                Math.Max(0, right - left - borderThickness),
                Math.Max(0, BarHeight - borderThickness));

            if (shapeRect.Width <= 0 || shapeRect.Height <= 0)
            {
                return;
            }

            drawingContext.DrawRoundedRectangle(
                ResolveBrush(segment.DisplayColor),
                pen,
                shapeRect,
                radius,
                radius);

            if (!segment.ShowText)
            {
                return;
            }

            var contentLeft = borderThickness + (segment.Lane == 0 ? 26 : 2);
            if (segment.ShowTodoIndicator)
            {
                DrawTodoIndicator(drawingContext, segment, contentLeft, top, pixelsPerDip);
                contentLeft += TodoIndicatorSize + TodoIndicatorGap;
            }

            DrawEventText(drawingContext, segment, contentLeft, top, borderThickness, pixelsPerDip);
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    private static void DrawTodoIndicator(
        DrawingContext drawingContext,
        CalendarEventSegment segment,
        double left,
        double top,
        double pixelsPerDip)
    {
        var indicatorTop = top + (BarHeight - TodoIndicatorSize) / 2;
        var indicatorRect = new Rect(
            left + TodoBorderPen.Thickness / 2,
            indicatorTop + TodoBorderPen.Thickness / 2,
            TodoIndicatorSize - TodoBorderPen.Thickness,
            TodoIndicatorSize - TodoBorderPen.Thickness);

        drawingContext.DrawRectangle(Brushes.White, TodoBorderPen, indicatorRect);

        if (string.IsNullOrEmpty(segment.TodoCheckGlyph))
        {
            return;
        }

        var checkText = new FormattedText(
            segment.TodoCheckGlyph,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            TodoTypeface,
            11,
            TodoCheckBrush,
            pixelsPerDip);

        drawingContext.DrawText(checkText, new Point(left - 1, indicatorTop - 2));
    }

    private void DrawEventText(
        DrawingContext drawingContext,
        CalendarEventSegment segment,
        double left,
        double top,
        double borderThickness,
        double pixelsPerDip)
    {
        var availableWidth = RenderSize.Width - left - borderThickness - 2;
        if (availableWidth <= 0 || string.IsNullOrEmpty(segment.DisplayText))
        {
            return;
        }

        var fontSize = double.IsFinite(EventFontSize) && EventFontSize > 0
            ? EventFontSize
            : 12d;

        var text = new FormattedText(
            segment.DisplayText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            EventTypeface,
            fontSize,
            ResolveBrush(segment.DisplayForegroundColor),
            pixelsPerDip)
        {
            MaxTextWidth = availableWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

        var textTop = top + Math.Max(0, (BarHeight - text.Height) / 2);
        drawingContext.DrawText(text, new Point(left, textTop));
    }

    private static void OnSegmentsChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var layer = (MonthEventLayer)dependencyObject;
        layer.DetachSubscriptions();
        if (layer.IsLoaded)
        {
            layer.AttachSubscriptions();
        }

        layer._toolTipSegment = null;
        layer.ToolTip = null;
        layer.InvalidateVisual();
    }

    private void MonthEventLayer_Loaded(object sender, RoutedEventArgs e)
    {
        AttachSubscriptions();
        InvalidateVisual();
    }

    private void MonthEventLayer_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachSubscriptions();
        _toolTipSegment = null;
        ToolTip = null;
    }

    private void AttachSubscriptions()
    {
        var segments = Segments;
        if (segments is null || ReferenceEquals(_subscribedSegments, segments))
        {
            return;
        }

        DetachSubscriptions();

        _subscribedSegments = segments;
        if (segments is INotifyCollectionChanged collection)
        {
            _subscribedCollection = collection;
            collection.CollectionChanged += Segments_CollectionChanged;
        }

        RebuildSegmentSubscriptions();
    }

    private void DetachSubscriptions()
    {
        if (_subscribedCollection is not null)
        {
            _subscribedCollection.CollectionChanged -= Segments_CollectionChanged;
        }

        foreach (var segment in _observedSegments)
        {
            segment.PropertyChanged -= Segment_PropertyChanged;
        }

        _observedSegments.Clear();
        _subscribedCollection = null;
        _subscribedSegments = null;
    }

    private void Segments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildSegmentSubscriptions();
        _toolTipSegment = null;
        ToolTip = null;
        InvalidateVisual();
    }

    private void RebuildSegmentSubscriptions()
    {
        foreach (var segment in _observedSegments)
        {
            segment.PropertyChanged -= Segment_PropertyChanged;
        }

        _observedSegments.Clear();

        if (_subscribedSegments is null)
        {
            return;
        }

        foreach (var segment in _subscribedSegments)
        {
            if (_observedSegments.Add(segment))
            {
                segment.PropertyChanged += Segment_PropertyChanged;
            }
        }
    }

    private void Segment_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName == nameof(CalendarEventSegment.IsSelected))
        {
            InvalidateVisual();
        }
    }

    private static Pen CreateFrozenPen(string color, double thickness)
    {
        var pen = new Pen(ResolveBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Brush ResolveBrush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Brushes.Transparent;
        }

        return BrushCache.GetOrAdd(value, static colorText =>
        {
            try
            {
                if (ColorConverter.ConvertFromString(colorText) is not Color color)
                {
                    return Brushes.Transparent;
                }

                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch (FormatException)
            {
                return Brushes.Transparent;
            }
            catch (NotSupportedException)
            {
                return Brushes.Transparent;
            }
        });
    }
}
