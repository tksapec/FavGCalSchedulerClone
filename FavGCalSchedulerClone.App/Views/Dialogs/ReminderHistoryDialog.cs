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
            Width = 1120,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false
        };
        var panel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        window.Content = panel;

        var detail = new Button { Content = "詳細", MinWidth = 96, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "閉じる", MinWidth = 96, Height = 28 };
        close.Click += (_, _) => window.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(detail);
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
        detail.Click += (_, _) =>
        {
            if (grid.SelectedItem is ReminderHistoryItem item)
            {
                ShowDetail(window, item);
            }
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "通知日時", Binding = new Binding(nameof(ReminderHistoryItem.NotifiedAtText)), Width = 140 });
        grid.Columns.Add(new DataGridTextColumn { Header = "種別", Binding = new Binding(nameof(ReminderHistoryItem.KindText)), Width = 70 });
        grid.Columns.Add(new DataGridTextColumn { Header = "予定日時", Binding = new Binding(nameof(ReminderHistoryItem.DateDisplayText)), Width = 140 });
        grid.Columns.Add(new DataGridTextColumn { Header = "件名", Binding = new Binding(nameof(ReminderHistoryItem.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "スヌーズ", Binding = new Binding(nameof(ReminderHistoryItem.SnoozedUntilText)), Width = 140 });
        grid.Columns.Add(new DataGridTextColumn { Header = "結果", Binding = new Binding(nameof(ReminderHistoryItem.DeliverySucceededText)), Width = 80 });
        grid.Columns.Add(new DataGridTextColumn { Header = "通知方式", Binding = new Binding(nameof(ReminderHistoryItem.DeliveryMethodText)), Width = 130 });
        grid.Columns.Add(new DataGridTextColumn { Header = "MessageBox", Binding = new Binding(nameof(ReminderHistoryItem.MessageBoxRoleText)), Width = 140 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Toast", Binding = new Binding(nameof(ReminderHistoryItem.ToastStatusText)), Width = 220 });
        grid.Columns.Add(new DataGridTextColumn { Header = "音声", Binding = new Binding(nameof(ReminderHistoryItem.SoundStatusText)), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = "エラー", Binding = new Binding(nameof(ReminderHistoryItem.ErrorText)), Width = 220 });
        panel.Children.Add(grid);

        window.ShowDialog();
    }

    private static void ShowDetail(Window owner, ReminderHistoryItem item)
    {
        var window = new Window
        {
            Owner = owner,
            Title = "通知履歴詳細",
            Width = 620,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        window.Content = root;

        var close = new Button { Content = "閉じる", MinWidth = 96, Height = 28, IsDefault = true };
        close.Click += (_, _) => window.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var text = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Join(Environment.NewLine, new[]
            {
                $"件名: {item.Title}",
                $"通知日時: {item.NotifiedAtText}",
                $"予定日時: {item.DateDisplayText}",
                $"結果: {item.DeliverySucceededText}",
                $"通知方式: {item.DeliveryMethodText}",
                $"MessageBox: {item.MessageBoxRoleText}",
                $"Toast: {item.ToastStatusText}",
                $"音声: {item.SoundStatusText}",
                $"失敗集約回数: {(item.FailureCount <= 0 ? 0 : item.FailureCount)}",
                $"最終失敗: {(item.LastFailedAt is null ? "" : item.LastFailedAt.Value.ToString("yyyy/MM/dd HH:mm:ss"))}",
                $"エラー: {item.ErrorText}",
                $"Status: {item.DeliveryStatusText}"
            })
        };
        root.Children.Add(text);
        window.ShowDialog();
    }
}
