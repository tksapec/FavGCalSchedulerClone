using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class ReminderHistoryDialog
{
    public static async Task ShowAsync(
        Window owner,
        Func<Task<(IReadOnlyList<ReminderHistoryItem> History, ReminderMonitoringSnapshot Diagnostics)>> loadAsync,
        Func<Task> checkNowAsync,
        Func<ReminderHistoryItem, Task> openAsync)
    {
        var historyItems = new ObservableCollection<ReminderHistoryItem>();
        var candidateItems = new ObservableCollection<ReminderCandidateDiagnostic>();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        var window = new Window
        {
            Owner = owner,
            Title = "通知履歴・診断",
            Width = 1180,
            Height = 650,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        window.Content = root;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var checkNow = new Button { Content = "通知判定を今すぐ実行", MinWidth = 160, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "閉じる", MinWidth = 96, Height = 30 };
        close.Click += (_, _) => window.Close();
        buttons.Children.Add(checkNow);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        DockPanel.SetDock(status, Dock.Top);
        root.Children.Add(status);

        var historyGrid = CreateHistoryGrid(historyItems, openAsync, window);
        var candidateGrid = CreateCandidateGrid(candidateItems);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "通知履歴", Content = historyGrid });
        tabs.Items.Add(new TabItem { Header = "通知候補診断", Content = candidateGrid });
        root.Children.Add(tabs);

        async Task ReloadAsync()
        {
            var data = await loadAsync();
            historyItems.Clear();
            foreach (var item in data.History) historyItems.Add(item);
            candidateItems.Clear();
            foreach (var item in data.Diagnostics.Candidates) candidateItems.Add(item);
            status.Text = FormatStatus(data.Diagnostics);
        }

        checkNow.Click += async (_, _) =>
        {
            checkNow.IsEnabled = false;
            try
            {
                await checkNowAsync();
                await ReloadAsync();
            }
            finally
            {
                checkNow.IsEnabled = true;
            }
        };

        await ReloadAsync();
        window.ShowDialog();
    }

    private static DataGrid CreateHistoryGrid(ObservableCollection<ReminderHistoryItem> items, Func<ReminderHistoryItem, Task> openAsync, Window window)
    {
        var grid = new DataGrid { ItemsSource = items, AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true, RowHeight = 24 };
        grid.MouseDoubleClick += async (_, _) =>
        {
            if (grid.SelectedItem is ReminderHistoryItem item)
            {
                await openAsync(item);
                window.Close();
            }
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "通知日時", Binding = new Binding(nameof(ReminderHistoryItem.NotifiedAtText)), Width = 140 });
        grid.Columns.Add(new DataGridTextColumn { Header = "件名", Binding = new Binding(nameof(ReminderHistoryItem.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "予定日時", Binding = new Binding(nameof(ReminderHistoryItem.DateDisplayText)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "結果", Binding = new Binding(nameof(ReminderHistoryItem.DeliverySucceededText)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "通知方式", Binding = new Binding(nameof(ReminderHistoryItem.DeliveryMethodText)), Width = 140 });
        grid.Columns.Add(new DataGridTextColumn { Header = "エラー", Binding = new Binding(nameof(ReminderHistoryItem.ErrorText)), Width = 260 });
        return grid;
    }

    private static DataGrid CreateCandidateGrid(ObservableCollection<ReminderCandidateDiagnostic> items)
    {
        var grid = new DataGrid { ItemsSource = items, AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true, RowHeight = 24 };
        grid.Columns.Add(new DataGridTextColumn { Header = "件名", Binding = new Binding(nameof(ReminderCandidateDiagnostic.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "予定開始", Binding = new Binding(nameof(ReminderCandidateDiagnostic.EventStart)) { StringFormat = "yyyy/MM/dd HH:mm:ss" }, Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "通知分", Binding = new Binding(nameof(ReminderCandidateDiagnostic.ReminderMinutesBeforeStart)), Width = 70 });
        grid.Columns.Add(new DataGridTextColumn { Header = "通知予定", Binding = new Binding(nameof(ReminderCandidateDiagnostic.RemindAt)) { StringFormat = "yyyy/MM/dd HH:mm:ss" }, Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "判定理由", Binding = new Binding(nameof(ReminderCandidateDiagnostic.Reason)), Width = 150 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "期限到達", Binding = new Binding(nameof(ReminderCandidateDiagnostic.IsDue)), Width = 75 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "発火済み", Binding = new Binding(nameof(ReminderCandidateDiagnostic.IsFired)), Width = 75 });
        grid.Columns.Add(new DataGridTextColumn { Header = "スヌーズ期限", Binding = new Binding(nameof(ReminderCandidateDiagnostic.SnoozedUntil)) { StringFormat = "yyyy/MM/dd HH:mm:ss" }, Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "OccurrenceKey", Binding = new Binding(nameof(ReminderCandidateDiagnostic.OccurrenceKey)), Width = 240 });
        return grid;
    }

    private static string FormatStatus(ReminderMonitoringSnapshot value)
    {
        return $"通知監視サービス: {(value.IsRunning ? "起動中" : "停止中")}  " +
               $"最終チェック: {FormatDate(value.LastCheckAt)}  次回チェック: {FormatDate(value.NextCheckAt)}\n" +
               $"保存予定: {value.StoredEventsCount}  展開後: {value.ExpandedEventsCount}  通知設定あり: {value.ReminderConfiguredCount}  " +
               $"候補: {value.CandidateCount}  通知対象: {value.DueCount}  fired除外: {value.FiredExcludedCount}  snooze除外: {value.SnoozedExcludedCount}\n" +
               $"成功: {value.SucceededCount}  失敗: {value.FailedCount}  最後の通知エラー: {value.LastError ?? "なし"}";
    }

    private static string FormatDate(DateTimeOffset? value) => value?.ToString("yyyy/MM/dd HH:mm:ss") ?? "未実行";
}
