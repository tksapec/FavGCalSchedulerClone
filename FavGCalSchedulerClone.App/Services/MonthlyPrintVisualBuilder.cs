using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FavGCalSchedulerClone.App.Services;

public static class MonthlyPrintVisualBuilder
{
    private static readonly string[] DayHeaders = ["日", "月", "火", "水", "木", "金", "土"];

    public static FrameworkElement Build(MonthlyPrintPlan plan, double width, double height)
    {
        var root = new Grid
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            Margin = new Thickness(0)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new TextBlock
        {
            Text = $"{plan.Title} 月間予定表",
            FontFamily = new FontFamily("Meiryo UI"),
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Margin = new Thickness(26, 20, 26, 12),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        root.Children.Add(title);

        var calendar = new Grid
        {
            Margin = new Thickness(26, 0, 26, 24),
            Background = new SolidColorBrush(Color.FromRgb(203, 213, 225))
        };
        Grid.SetRow(calendar, 1);
        root.Children.Add(calendar);

        for (var column = 0; column < 7; column++)
        {
            calendar.ColumnDefinitions.Add(new ColumnDefinition());
        }

        calendar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        for (var row = 0; row < 6; row++)
        {
            calendar.RowDefinitions.Add(new RowDefinition());
        }

        for (var column = 0; column < 7; column++)
        {
            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new TextBlock
                {
                    Text = DayHeaders[column],
                    FontFamily = new FontFamily("Meiryo UI"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = HeaderForeground(column),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(header, column);
            calendar.Children.Add(header);
        }

        for (var index = 0; index < plan.Days.Count; index++)
        {
            var day = plan.Days[index];
            var dayCell = CreateDayCell(day);
            Grid.SetColumn(dayCell, index % 7);
            Grid.SetRow(dayCell, (index / 7) + 1);
            calendar.Children.Add(dayCell);
        }

        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        return root;
    }

    private static Border CreateDayCell(MonthlyPrintDay day)
    {
        var panel = new DockPanel { LastChildFill = true, Margin = new Thickness(5) };
        var dateText = new TextBlock
        {
            Text = day.Date.Day.ToString(CultureInfo.InvariantCulture),
            FontFamily = new FontFamily("Meiryo UI"),
            FontSize = 13,
            FontWeight = day.IsToday ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = DayForeground(day),
            Margin = new Thickness(0, 0, 0, 3)
        };
        DockPanel.SetDock(dateText, Dock.Top);
        panel.Children.Add(dateText);

        var entries = new StackPanel();
        foreach (var entry in day.Entries)
        {
            entries.Children.Add(new Border
            {
                Background = BrushFromHex(entry.DisplayColor, Color.FromRgb(241, 245, 249)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(3, 0, 3, 1),
                Child = new TextBlock
                {
                    Text = entry.Text,
                    FontFamily = new FontFamily("Meiryo UI"),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap
                }
            });
        }

        if (day.HiddenEntryCount > 0)
        {
            entries.Children.Add(new TextBlock
            {
                Text = $"ほか {day.HiddenEntryCount} 件",
                FontFamily = new FontFamily("Meiryo UI"),
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(2, 1, 0, 0)
            });
        }

        panel.Children.Add(entries);

        return new Border
        {
            Background = DayBackground(day),
            BorderBrush = day.IsToday
                ? new SolidColorBrush(Color.FromRgb(37, 99, 235))
                : new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = day.IsToday ? new Thickness(2) : new Thickness(0, 0, 1, 1),
            Child = panel
        };
    }

    private static Brush HeaderForeground(int column)
    {
        return column switch
        {
            0 => new SolidColorBrush(Color.FromRgb(185, 28, 28)),
            6 => new SolidColorBrush(Color.FromRgb(29, 78, 216)),
            _ => new SolidColorBrush(Color.FromRgb(51, 65, 85))
        };
    }

    private static Brush DayForeground(MonthlyPrintDay day)
    {
        if (!day.IsCurrentMonth)
        {
            return new SolidColorBrush(Color.FromRgb(100, 116, 139));
        }

        if (day.IsSunday)
        {
            return new SolidColorBrush(Color.FromRgb(185, 28, 28));
        }

        if (day.IsSaturday)
        {
            return new SolidColorBrush(Color.FromRgb(29, 78, 216));
        }

        return new SolidColorBrush(Color.FromRgb(30, 41, 59));
    }

    private static Brush DayBackground(MonthlyPrintDay day)
    {
        if (!day.IsCurrentMonth)
        {
            return new SolidColorBrush(Color.FromRgb(248, 250, 252));
        }

        if (day.IsSunday)
        {
            return new SolidColorBrush(Color.FromRgb(255, 241, 242));
        }

        if (day.IsSaturday)
        {
            return new SolidColorBrush(Color.FromRgb(239, 246, 255));
        }

        return Brushes.White;
    }

    private static Brush BrushFromHex(string? hex, Color fallback)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                if (ColorConverter.ConvertFromString(hex) is Color color)
                {
                    return new SolidColorBrush(color);
                }
            }
            catch (FormatException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        return new SolidColorBrush(fallback);
    }
}
