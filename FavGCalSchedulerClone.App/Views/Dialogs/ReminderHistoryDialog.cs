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
        Func<Task> createTwoMinuteTestEventAsync,
        Func<Task<int>>? refreshGoogleRemindersAsync,
        Func<ReminderHistoryItem, Task> openAsync)
    {
        var historyItems = new ObservableCollection<ReminderHistoryItem>();
        var candidateItems = new ObservableCollection<ReminderCandidateDiagnostic>();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        var reasonSummary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), FontWeight = FontWeights.SemiBold };
        var window = new Window
        {
            Owner = owner,
            Title = "通知センター",
            Width = 1180,
            Height = 650,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        window.Content = root;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var createTest = new Button { Content = "2分後テスト予定を作成", MinWidth = 160, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        var checkNow = new Button { Content = "通知判定を今すぐ実行", MinWidth = 160, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "閉じる", MinWidth = 96, Height = 30 };
        var refreshGoogle = new Button { Content = "Google通知設定を再取得", MinWidth = 160, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsEnabled = refreshGoogleRemindersAsync is not null };
        close.Click += (_, _) => window.Close();
        buttons.Children.Add(createTest);
        buttons.Children.Add(checkNow);
        buttons.Children.Add(refreshGoogle);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        DockPanel.SetDock(status, Dock.Top);
        root.Children.Add(status);
        DockPanel.SetDock(reasonSummary, Dock.Top);
        root.Children.Add(reasonSummary);

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
            reasonSummary.Text = FormatReasonSummary(data.Diagnostics);
        }

        checkNow.Click += (_, _) => DialogAsyncGuard.Run(window, async () =>
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
        }, "通知判定");

        createTest.Click += (_, _) => DialogAsyncGuard.Run(window, async () =>
        {
            createTest.IsEnabled = false;
            try
            {
                await createTwoMinuteTestEventAsync();
                await ReloadAsync();
            }
            finally
            {
                createTest.IsEnabled = true;
            }
        }, "通知テスト予定作成");

        refreshGoogle.Click += (_, _) => DialogAsyncGuard.Run(window, async () =>
        {
            if (refreshGoogleRemindersAsync is null)
            {
                return;
            }

            refreshGoogle.IsEnabled = false;
            try
            {
                await refreshGoogleRemindersAsync();
                await ReloadAsync();
            }
            finally
            {
                refreshGoogle.IsEnabled = true;
            }
        }, "Google通知設定更新");

        await ReloadAsync();
        window.ShowDialog();
    }

    private static DataGrid CreateHistoryGrid(ObservableCollection<ReminderHistoryItem> items, Func<ReminderHistoryItem, Task> openAsync, Window window)
    {
        var grid = new DataGrid { ItemsSource = items, AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true, RowHeight = 24 };
        grid.MouseDoubleClick += (_, _) => DialogAsyncGuard.Run(window, async () =>
        {
            if (grid.SelectedItem is ReminderHistoryItem item)
            {
                await openAsync(item);
                window.Close();
            }
        }, "通知履歴を開く");
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
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "G既定", Binding = new Binding(nameof(ReminderCandidateDiagnostic.GoogleReminderUseDefault)), Width = 60 });
        grid.Columns.Add(new DataGridTextColumn { Header = "G popup", Binding = new Binding(nameof(ReminderCandidateDiagnostic.GooglePopupReminderText)), Width = 120 });
        grid.Columns.Add(new DataGridTextColumn { Header = "G email", Binding = new Binding(nameof(ReminderCandidateDiagnostic.GoogleEmailReminderText)), Width = 120 });
        grid.Columns.Add(new DataGridTextColumn { Header = "G既定通知", Binding = new Binding(nameof(ReminderCandidateDiagnostic.GoogleDefaultReminderText)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "採用通知分", Binding = new Binding(nameof(ReminderCandidateDiagnostic.AdoptedGoogleReminderMinutes)), Width = 80 });
        grid.Columns.Add(new DataGridTextColumn { Header = "差分", Binding = new Binding(nameof(ReminderCandidateDiagnostic.ReminderDifferenceText)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "通知予定", Binding = new Binding(nameof(ReminderCandidateDiagnostic.RemindAt)) { StringFormat = "yyyy/MM/dd HH:mm:ss" }, Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "判定理由", Binding = new Binding(nameof(ReminderCandidateDiagnostic.Reason)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "エラー", Binding = new Binding(nameof(ReminderCandidateDiagnostic.ErrorMessage)), Width = 240 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "期限到達", Binding = new Binding(nameof(ReminderCandidateDiagnostic.IsDue)), Width = 75 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "発火済み", Binding = new Binding(nameof(ReminderCandidateDiagnostic.IsFired)), Width = 75 });
        grid.Columns.Add(new DataGridTextColumn { Header = "スヌーズ期限", Binding = new Binding(nameof(ReminderCandidateDiagnostic.SnoozedUntil)) { StringFormat = "yyyy/MM/dd HH:mm:ss" }, Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "OccurrenceKey", Binding = new Binding(nameof(ReminderCandidateDiagnostic.OccurrenceKey)), Width = 240 });
        return grid;
    }

    private static string FormatReasonSummary(ReminderMonitoringSnapshot value)
    {
        if (value.Candidates.Count == 0)
        {
            return "通知候補はありません。手動判定を実行すると詳細候補を確認できます。";
        }

        var reasons = value.Candidates
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Reason) ? "理由なし" : item.Reason)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCulture)
            .Take(4)
            .Select(group => $"{group.Key}: {group.Count()}件");
        return "通知されない理由サマリー: " + string.Join(" / ", reasons);
    }

    private static string FormatStatus(ReminderMonitoringSnapshot value)
    {
        return $"通知監視サービス: {(value.IsRunning ? "起動中" : "停止中")}  " +
               $"最終チェック: {FormatDate(value.LastCheckAt)}  次回チェック: {FormatDate(value.NextCheckAt)}\n" +
               $"保存予定: {value.StoredEventsCount}  展開後: {value.ExpandedEventsCount}  通知設定あり: {value.ReminderConfiguredCount}  通知設定なし: {value.NoReminderCount}  " +
               $"候補: {value.CandidateCount}  通知対象: {value.DueCount}  fired除外: {value.FiredExcludedCount}  snooze除外: {value.SnoozedExcludedCount}\n" +
               $"成功: {value.SucceededCount}  失敗: {value.FailedCount}  最後の通知エラー: {value.LastError ?? "なし"}";
    }

    private static string FormatDate(DateTimeOffset? value) => value?.ToString("yyyy/MM/dd HH:mm:ss") ?? "未実行";
}
