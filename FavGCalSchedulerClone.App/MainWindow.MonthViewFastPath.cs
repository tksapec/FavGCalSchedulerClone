using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FavGCalSchedulerClone.App.Controls;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.App;

public partial class MainWindow
{
    private bool _monthViewFastTemplateInstalled;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property != DataContextProperty
            || _monthViewFastTemplateInstalled
            || e.NewValue is not MainViewModel
            || DayList is null)
        {
            return;
        }

        DayList.MouseDoubleClick -= DayList_MouseDoubleClick;
        DayList.MouseDoubleClick += MonthDayList_MouseDoubleClick;
        DayList.ItemTemplate = CreateFastMonthDayTemplate();
        _monthViewFastTemplateInstalled = true;
    }

    private DataTemplate CreateFastMonthDayTemplate()
    {
        var root = new FrameworkElementFactory(typeof(Border));
        root.SetResourceReference(FrameworkElement.StyleProperty, "DayCell");
        root.SetValue(UIElement.AllowDropProperty, true);
        root.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(CalendarDay.DayToolTipText)));
        root.AddHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler(DayCell_MouseLeftButtonDown));
        root.AddHandler(UIElement.MouseRightButtonDownEvent, new MouseButtonEventHandler(DayCell_MouseRightButtonDown));
        root.AddHandler(UIElement.DragEnterEvent, new DragEventHandler(DayCell_DragOver));
        root.AddHandler(UIElement.DragOverEvent, new DragEventHandler(DayCell_DragOver));
        root.AddHandler(UIElement.DragLeaveEvent, new DragEventHandler(DayCell_DragLeave));
        root.AddHandler(UIElement.DropEvent, new DragEventHandler(DayCell_Drop));

        var layout = new FrameworkElementFactory(typeof(Grid));
        root.AppendChild(layout);

        var eventLayer = new FrameworkElementFactory(typeof(MonthEventLayer));
        eventLayer.SetBinding(MonthEventLayer.SegmentsProperty, new Binding(nameof(CalendarDay.Segments)));
        eventLayer.SetBinding(
            MonthEventLayer.EventFontSizeProperty,
            new Binding("DataContext.CalendarLabelFontSize")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1)
            });
        eventLayer.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        eventLayer.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        eventLayer.SetValue(UIElement.ClipToBoundsProperty, true);
        eventLayer.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(MonthEventLayer_PreviewMouseLeftButtonDown));
        eventLayer.AddHandler(
            UIElement.PreviewMouseMoveEvent,
            new MouseEventHandler(MonthEventLayer_PreviewMouseMove));
        eventLayer.AddHandler(
            UIElement.MouseLeftButtonDownEvent,
            new MouseButtonEventHandler(MonthEventLayer_MouseLeftButtonDown));
        eventLayer.AddHandler(
            UIElement.MouseRightButtonDownEvent,
            new MouseButtonEventHandler(MonthEventLayer_MouseRightButtonDown));
        layout.AppendChild(eventLayer);

        layout.AppendChild(CreateDateBadgeFactory());
        layout.AppendChild(CreateHiddenEventBadgeFactory());
        layout.AppendChild(CreateSelectionOverlayFactory());

        return new DataTemplate(typeof(CalendarDay))
        {
            VisualTree = root
        };
    }

    private static FrameworkElementFactory CreateDateBadgeFactory()
    {
        var badge = new FrameworkElementFactory(typeof(Border));
        badge.SetValue(Panel.ZIndexProperty, 10);
        badge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        badge.SetValue(FrameworkElement.MinWidthProperty, 24d);
        badge.SetValue(FrameworkElement.HeightProperty, 16d);
        badge.SetValue(Border.BackgroundProperty, CreateFrozenBrush("#CCFFFFFF"));
        badge.SetValue(UIElement.IsHitTestVisibleProperty, false);
        badge.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 0, 0));

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("Date.Day"));
        text.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(CalendarDay.DayToolTipText)));
        text.SetValue(TextBlock.ForegroundProperty, CreateFrozenBrush("#334155"));
        text.SetValue(TextBlock.FontSizeProperty, 11d);
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        text.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        badge.AppendChild(text);
        return badge;
    }

    private static FrameworkElementFactory CreateHiddenEventBadgeFactory()
    {
        var badge = new FrameworkElementFactory(typeof(Border));
        badge.SetValue(Panel.ZIndexProperty, 20);
        badge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        badge.SetValue(Border.BackgroundProperty, CreateFrozenBrush("#CCFFFFFF"));
        badge.SetValue(UIElement.IsHitTestVisibleProperty, false);
        badge.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 2, 1));
        badge.SetBinding(
            UIElement.VisibilityProperty,
            new Binding(nameof(CalendarDay.HasHiddenEvents))
            {
                Converter = new BooleanToVisibilityConverter()
            });

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(CalendarDay.HiddenEventText)));
        text.SetValue(TextBlock.ForegroundProperty, CreateFrozenBrush("#B45309"));
        text.SetValue(TextBlock.FontSizeProperty, 11d);
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0));
        badge.AppendChild(text);
        return badge;
    }

    private static FrameworkElementFactory CreateSelectionOverlayFactory()
    {
        var overlay = new FrameworkElementFactory(typeof(Border));
        overlay.SetValue(Panel.ZIndexProperty, 30);
        overlay.SetValue(UIElement.IsHitTestVisibleProperty, false);
        overlay.SetValue(FrameworkElement.StyleProperty, CreateSelectionOverlayStyle());
        return overlay;
    }

    private static Style CreateSelectionOverlayStyle()
    {
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0)));

        var today = new DataTrigger
        {
            Binding = new Binding(nameof(CalendarDay.IsToday)),
            Value = true
        };
        today.Setters.Add(new Setter(Border.BorderBrushProperty, CreateFrozenBrush("#2563EB")));
        today.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2)));
        style.Triggers.Add(today);

        var hover = new DataTrigger
        {
            Binding = new Binding(nameof(UIElement.IsMouseOver))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Border), 1)
            },
            Value = true
        };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, CreateFrozenBrush("#3B82F6")));
        hover.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2)));
        style.Triggers.Add(hover);

        var selected = new DataTrigger
        {
            Binding = new Binding(nameof(ListBoxItem.IsSelected))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1)
            },
            Value = true
        };
        selected.Setters.Add(new Setter(Border.BorderBrushProperty, CreateFrozenBrush("#0F766E")));
        selected.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(3)));
        style.Triggers.Add(selected);

        var dropTarget = new DataTrigger
        {
            Binding = new Binding(nameof(CalendarDay.IsDropTarget)),
            Value = true
        };
        dropTarget.Setters.Add(new Setter(Border.BorderBrushProperty, CreateFrozenBrush("#F59E0B")));
        dropTarget.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(3)));
        style.Triggers.Add(dropTarget);

        return style;
    }

    private static Brush CreateFrozenBrush(string value)
    {
        if (ColorConverter.ConvertFromString(value) is not Color color)
        {
            return Brushes.Transparent;
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
