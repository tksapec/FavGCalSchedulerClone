using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class ReminderHistoryDialog
{
    public static void Show(Window owner, IReadOnlyList<ReminderHistoryItem> history, Func<ReminderHistoryItem, Task> openAsync)
    {
        var window = new Window
        {
            Owner = owner,
            Title = "通知一覧",
            Width = 760,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false
        };
        var panel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        window.Content = panel;

        var close = new Button { Content = "閉じる", MinWidth = 96, Height = 28 };
        close.Click += (_, _) => window.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        var grid = new DataGrid
        {
            ItemsSource = new ObservableCollection<ReminderHistoryItem>(history),
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 24
        };
        grid.MouseDoubleClick += async (_, _) =>
        {
            if (grid.SelectedItem is ReminderHistoryItem item)
            {
                await openAsync(item);
                window.Close();
            }
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "通知日時", Binding = new Binding(nameof(ReminderHistoryItem.NotifiedAtText)), Width = 140 });
        grid.Columns.Add(new DataGridTextColumn { Header = "種別", Binding = new Binding(nameof(ReminderHistoryItem.KindText)), Width = 70 });
        grid.Columns.Add(new DataGridTextColumn { Header = "予定日時", Binding = new Binding(nameof(ReminderHistoryItem.DateDisplayText)), Width = 140 });
        grid.Columns.Add(new DataGridTextColumn { Header = "件名", Binding = new Binding(nameof(ReminderHistoryItem.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "スヌーズ", Binding = new Binding(nameof(ReminderHistoryItem.SnoozedUntilText)), Width = 140 });
        panel.Children.Add(grid);

        window.ShowDialog();
    }
}
