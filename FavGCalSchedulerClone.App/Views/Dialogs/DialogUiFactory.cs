using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal sealed class DialogUiFactory
{
    private readonly Window _owner;
    private readonly IReadOnlyList<EventColorSelectionItem> _eventColorOptions;
    private readonly double _sideListFontSize;

    public DialogUiFactory(Window owner, IReadOnlyList<EventColorSelectionItem> eventColorOptions, double sideListFontSize)
    {
        _owner = owner;
        _eventColorOptions = eventColorOptions;
        _sideListFontSize = sideListFontSize;
    }

    public Window CreateOwnedDialog(string title, double width, double height, bool usePhysicalPixelSize = false)
    {
        if (usePhysicalPixelSize)
        {
            width = X(width);
            height = Y(height);
        }

        return new Window
        {
            Owner = _owner,
            Title = title,
            Width = width,
            Height = height,
            MinWidth = width,
            MinHeight = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };
    }

    public StackPanel CreateDialogRoot()
    {
        return new StackPanel
        {
            Margin = new Thickness(16),
            Orientation = Orientation.Vertical
        };
    }

    public Grid CreateEditorDialogRoot()
    {
        var grid = new Grid { Margin = Thickness(10, 10, 10, 10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    public double X(double physicalPixels) => physicalPixels / VisualTreeHelper.GetDpi(_owner).DpiScaleX;

    public double Y(double physicalPixels) => physicalPixels / VisualTreeHelper.GetDpi(_owner).DpiScaleY;

    public Thickness Thickness(double left, double top, double right, double bottom) =>
        new(X(left), Y(top), X(right), Y(bottom));

    public ComboBox CreateColorComboBox(string? selectedColorId)
    {
        var combo = new ComboBox
        {
            ItemsSource = _eventColorOptions,
            SelectedValuePath = nameof(EventColorSelectionItem.Id),
            SelectedValue = selectedColorId,
            MinWidth = X(200)
        };

        var template = new DataTemplate(typeof(EventColorSelectionItem));
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var color = new FrameworkElementFactory(typeof(Border));
        color.SetValue(Border.WidthProperty, X(54));
        color.SetValue(Border.HeightProperty, Y(14));
        color.SetValue(Border.BorderBrushProperty, Brushes.SlateGray);
        color.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        color.SetValue(Border.MarginProperty, new Thickness(0, 0, 8, 0));
        color.SetBinding(Border.BackgroundProperty, new Binding(nameof(EventColorSelectionItem.Background)));
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(EventColorSelectionItem.Label)));
        label.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(color);
        panel.AppendChild(label);
        template.VisualTree = panel;
        combo.ItemTemplate = template;
        combo.SelectedIndex = string.IsNullOrWhiteSpace(selectedColorId) ? 0 : combo.SelectedIndex;
        return combo;
    }

    public DatePicker CreateDatePickerWithTodayButton(DateTime selectedDate, out FrameworkElement editor)
    {
        var datePicker = new DatePicker
        {
            SelectedDate = selectedDate
        };
        var today = new Button
        {
            Content = "今日",
            MinWidth = X(58),
            Margin = new Thickness(X(8), 0, 0, 0)
        };
        today.Click += (_, _) => datePicker.SelectedDate = DateTime.Today;

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(today, Dock.Right);
        panel.Children.Add(today);
        panel.Children.Add(datePicker);
        editor = panel;
        return datePicker;
    }

    public DataGridTemplateColumn CreateColoredTitleColumn(DataGridLength width)
    {
        var template = new DataTemplate(typeof(CalendarEvent));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BorderBrushProperty, Brushes.SlateGray);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new Thickness(4, 1, 4, 1));
        border.SetValue(Border.MarginProperty, new Thickness(1));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(CalendarEvent.DisplayColor)));
        border.SetBinding(Border.ToolTipProperty, new Binding(nameof(CalendarEvent.ToolTipText)));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(CalendarEvent.Title)));
        text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(CalendarEvent.DisplayForegroundColor)));
        text.SetValue(TextBlock.FontSizeProperty, _sideListFontSize);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        border.AppendChild(text);
        template.VisualTree = border;
        return new DataGridTemplateColumn { Header = "件名", CellTemplate = template, Width = width };
    }

    public void AddLabeledField(
        Grid grid,
        int row,
        int column,
        string label,
        FrameworkElement input,
        int columnSpan = 1,
        bool stretchVertically = false,
        double rightMarginPhysicalPixels = 16)
    {
        var field = new Grid { Margin = Thickness(0, 0, rightMarginPhysicalPixels, 12) };
        field.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        field.RowDefinitions.Add(new RowDefinition
        {
            Height = stretchVertically ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
        });
        field.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, Y(6)) });
        input.Margin = new Thickness(0);
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (stretchVertically)
        {
            input.VerticalAlignment = VerticalAlignment.Stretch;
        }

        Grid.SetRow(input, 1);
        field.Children.Add(input);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, column);
        Grid.SetColumnSpan(field, columnSpan);
        grid.Children.Add(field);
    }

    public TextBlock SectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    public Grid FormGrid(params (string LeftLabel, FrameworkElement LeftInput, string RightLabel, FrameworkElement RightInput)[] rows)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddFormCell(grid, rows[rowIndex].LeftLabel, rows[rowIndex].LeftInput, rowIndex, 0);
            AddFormCell(grid, rows[rowIndex].RightLabel, rows[rowIndex].RightInput, rowIndex, 2);
        }

        return grid;
    }

    public FrameworkElement WideField(string label, FrameworkElement input)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(input);
        return panel;
    }

    public StackPanel DialogButtons(Window window, string okText, string cancelText)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var ok = new Button { Content = okText, MinWidth = 96 };
        ok.Click += (_, _) => window.DialogResult = true;
        var cancel = new Button { Content = cancelText, MinWidth = 96 };
        cancel.Click += (_, _) => window.DialogResult = false;

        panel.Children.Add(ok);
        panel.Children.Add(cancel);
        return panel;
    }

    private static void AddFormCell(Grid grid, string label, FrameworkElement input, int row, int column)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            var text = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 8)
            };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
        }

        input.Margin = new Thickness(0, 0, 12, 8);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, column + 1);
        grid.Children.Add(input);
    }
}
