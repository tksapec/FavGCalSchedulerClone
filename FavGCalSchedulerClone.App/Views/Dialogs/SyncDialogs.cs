using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class SyncDialogs
{
    public static bool? ShowPreview(Window owner, SyncPreview preview)
    {
        var window = CreateOwnedDialog(owner, "Google同期プレビュー", 780, 520);
        var panel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        window.Content = panel;

        var summary = new TextBlock
        {
            Text = $"送信 {preview.PushCount} 件 / 取得 {preview.PullCount} 件 / 削除 {preview.DeleteCount} 件 / 競合 {preview.ConflictCount} 件 / エラー {preview.ErrorCount} 件",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(summary, Dock.Top);
        panel.Children.Add(summary);

        var buttons = DialogButtons(window, "同期実行", "キャンセル");
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        var items = new ObservableCollection<SyncPreviewItem>(
            preview.PushItems
                .Concat(preview.PullItems)
                .Concat(preview.DeleteItems)
                .Concat(preview.ConflictItems)
                .Concat(preview.ErrorItems));
        var grid = new DataGrid
        {
            ItemsSource = items,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 24
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "種別", Binding = new Binding(nameof(SyncPreviewItem.Kind)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "カレンダー", Binding = new Binding(nameof(SyncPreviewItem.CalendarId)), Width = 120 });
        grid.Columns.Add(new DataGridTextColumn { Header = "開始", Binding = new Binding(nameof(SyncPreviewItem.Start)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "件名", Binding = new Binding(nameof(SyncPreviewItem.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "詳細", Binding = new Binding(nameof(SyncPreviewItem.Detail)), Width = 220 });
        panel.Children.Add(grid);

        return window.ShowDialog();
    }

    public static void ShowDiagnostics(Window owner, SyncDiagnosticsSnapshot diagnostics, Func<Task> clearAsync)
    {
        var window = CreateOwnedDialog(owner, "Google同期診断", 820, 540);
        var panel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        window.Content = panel;

        var last = diagnostics.LastResult;
        var summaryText = last is null
            ? $"未同期変更: {diagnostics.DirtyCount} 件\n最終同期結果はありません。"
            : $"未同期変更: {diagnostics.DirtyCount} 件\n最終同期: {last.FinishedAt:yyyy/MM/dd HH:mm:ss} / 送信 {last.Pushed} / 取得 {last.Pulled} / 競合 {last.Conflicts} / 失敗 {last.Failed}";
        var summary = new TextBlock { Text = summaryText, Margin = new Thickness(0, 0, 0, 8), FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(summary, Dock.Top);
        panel.Children.Add(summary);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var clear = new Button { Content = "ログ削除", MinWidth = 96, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "閉じる", MinWidth = 96, Height = 28 };
        clear.Click += async (_, _) =>
        {
            await clearAsync();
            window.Close();
        };
        close.Click += (_, _) => window.Close();
        buttons.Children.Add(clear);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        var tabs = new TabControl();
        var calendarGrid = new DataGrid
        {
            ItemsSource = new ObservableCollection<SyncCalendarDiagnostic>(diagnostics.Calendars),
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true
        };
        calendarGrid.Columns.Add(new DataGridTextColumn { Header = "カレンダー", Binding = new Binding(nameof(SyncCalendarDiagnostic.CalendarId)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        calendarGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "syncToken", Binding = new Binding(nameof(SyncCalendarDiagnostic.HasSyncToken)), Width = 90 });
        calendarGrid.Columns.Add(new DataGridTextColumn { Header = "未同期", Binding = new Binding(nameof(SyncCalendarDiagnostic.DirtyCount)), Width = 80 });
        tabs.Items.Add(new TabItem { Header = "カレンダー", Content = calendarGrid });

        var dirtyGrid = new DataGrid
        {
            ItemsSource = new ObservableCollection<SyncDirtyItem>(diagnostics.DirtyItems),
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true
        };
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "種別", Binding = new Binding(nameof(SyncDirtyItem.Kind)), Width = 70 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "操作", Binding = new Binding(nameof(SyncDirtyItem.Operation)), Width = 70 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "カレンダー", Binding = new Binding(nameof(SyncDirtyItem.CalendarId)), Width = 120 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "開始", Binding = new Binding(nameof(SyncDirtyItem.Start)), Width = 150 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "件名", Binding = new Binding(nameof(SyncDirtyItem.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "Google ID", Binding = new Binding(nameof(SyncDirtyItem.GoogleEventId)), Width = 140 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "更新", Binding = new Binding(nameof(SyncDirtyItem.UpdatedAt)), Width = 150 });
        tabs.Items.Add(new TabItem { Header = "未同期", Content = dirtyGrid });

        var historyGrid = new DataGrid
        {
            ItemsSource = new ObservableCollection<SyncResult>(diagnostics.History),
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true
        };
        historyGrid.Columns.Add(new DataGridTextColumn { Header = "終了", Binding = new Binding(nameof(SyncResult.FinishedAt)), Width = 160 });
        historyGrid.Columns.Add(new DataGridTextColumn { Header = "送信", Binding = new Binding(nameof(SyncResult.Pushed)), Width = 60 });
        historyGrid.Columns.Add(new DataGridTextColumn { Header = "取得", Binding = new Binding(nameof(SyncResult.Pulled)), Width = 60 });
        historyGrid.Columns.Add(new DataGridTextColumn { Header = "競合", Binding = new Binding(nameof(SyncResult.Conflicts)), Width = 60 });
        historyGrid.Columns.Add(new DataGridTextColumn { Header = "失敗", Binding = new Binding(nameof(SyncResult.Failed)), Width = 60 });
        historyGrid.Columns.Add(new DataGridTextColumn { Header = "詳細", Binding = new Binding(nameof(SyncResult.Message)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        tabs.Items.Add(new TabItem { Header = "ログ", Content = historyGrid });
        panel.Children.Add(tabs);

        window.ShowDialog();
    }

    private static Window CreateOwnedDialog(Window owner, string title, double width, double height) =>
        new()
        {
            Owner = owner,
            Title = title,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false
        };

    private static StackPanel DialogButtons(Window window, string okText, string cancelText)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = okText, MinWidth = 96, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = cancelText, MinWidth = 96, Height = 28, IsCancel = true };
        ok.Click += (_, _) =>
        {
            window.DialogResult = true;
            window.Close();
        };
        cancel.Click += (_, _) =>
        {
            window.DialogResult = false;
            window.Close();
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        return buttons;
    }
}
