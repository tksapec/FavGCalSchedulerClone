using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FavGCalSchedulerClone.App.Models;
using Microsoft.Win32;

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
            RowHeight = 24,
            RowStyle = CreatePreviewRowStyle()
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "種別", Binding = new Binding(nameof(SyncPreviewItem.Kind)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "カレンダー", Binding = new Binding(nameof(SyncPreviewItem.CalendarId)), Width = 120 });
        grid.Columns.Add(new DataGridTextColumn { Header = "開始", Binding = new Binding(nameof(SyncPreviewItem.Start)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "件名", Binding = new Binding(nameof(SyncPreviewItem.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "詳細", Binding = new Binding(nameof(SyncPreviewItem.Detail)), Width = 220 });
        grid.Columns.Add(new DataGridTextColumn { Header = "変更内容", Binding = new Binding(nameof(SyncPreviewItem.ChangeFieldsText)), Width = 160 });
        var fieldDiffItems = new ObservableCollection<SyncFieldDiff>();
        var fieldDiffGrid = new DataGrid
        {
            ItemsSource = fieldDiffItems,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            Height = 150,
            Margin = new Thickness(0, 8, 0, 0)
        };
        fieldDiffGrid.Columns.Add(new DataGridTextColumn { Header = "項目", Binding = new Binding(nameof(SyncFieldDiff.DisplayName)), Width = 100 });
        fieldDiffGrid.Columns.Add(new DataGridTextColumn { Header = "ローカル", Binding = new Binding(nameof(SyncFieldDiff.LocalValue)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        fieldDiffGrid.Columns.Add(new DataGridTextColumn { Header = "Google", Binding = new Binding(nameof(SyncFieldDiff.GoogleValue)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        fieldDiffGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "差分", Binding = new Binding(nameof(SyncFieldDiff.IsDifferent)), Width = 60 });
        grid.SelectionChanged += (_, _) =>
        {
            fieldDiffItems.Clear();
            if (grid.SelectedItem is SyncPreviewItem selected)
            {
                foreach (var diff in selected.FieldDiffs ?? [])
                {
                    fieldDiffItems.Add(diff);
                }
            }
        };
        DockPanel.SetDock(fieldDiffGrid, Dock.Bottom);
        panel.Children.Add(fieldDiffGrid);
        panel.Children.Add(grid);

        return window.ShowDialog();
    }

    public static void ShowDiagnostics(
        Window owner,
        SyncDiagnosticsSnapshot diagnostics,
        Func<Task<SyncDiagnosticsSnapshot>> reloadDiagnosticsAsync,
        Func<Task> clearAsync,
        Func<IReadOnlyList<string>, Task>? retryFailuresAsync = null,
        Func<string, Task>? openDirtyItemAsync = null,
        Func<IReadOnlyList<string>, Task>? retryDirtyItemsAsync = null,
        Func<IReadOnlyList<string>, Task>? markDirtyItemsSyncedAsync = null,
        Func<IReadOnlyList<string>, Task>? discardDirtyItemsAsync = null,
        Func<Task<int>>? refreshGoogleRemindersAsync = null)
    {
        var currentDiagnostics = diagnostics;
        var window = CreateOwnedDialog(owner, "Google同期センター", 820, 540);
        var panel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        window.Content = panel;

        var last = diagnostics.LastResult;
        var summaryText = last is null
            ? $"未同期変更: {diagnostics.DirtyCount} 件\n最終同期結果はありません。"
            : $"未同期変更: {diagnostics.DirtyCount} 件\n最終同期: {last.FinishedAt:yyyy/MM/dd HH:mm:ss} / {last.SummaryText}";
        var summary = new TextBlock { Text = BuildDiagnosticsSummary(diagnostics), Margin = new Thickness(0, 0, 0, 8), FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(summary, Dock.Top);
        panel.Children.Add(summary);

        var status = new TextBlock { Margin = new Thickness(0, 0, 0, 8), Foreground = System.Windows.Media.Brushes.DarkSlateGray };
        DockPanel.SetDock(status, Dock.Top);
        panel.Children.Add(status);

        var calendarItems = new ObservableCollection<SyncCalendarDiagnostic>(diagnostics.Calendars);
        var dirtyItems = new ObservableCollection<SyncDirtyItem>(diagnostics.DirtyItems);
        var failureItems = new ObservableCollection<SyncFailureDiagnostic>(diagnostics.Failures);
        var historyItems = new ObservableCollection<SyncResult>(diagnostics.History);

        async Task RunAndRefreshAsync(Func<Task> operation, string successMessage)
        {
            try
            {
                await operation();
                currentDiagnostics = await reloadDiagnosticsAsync();
                ReplaceAll(calendarItems, currentDiagnostics.Calendars);
                ReplaceAll(dirtyItems, currentDiagnostics.DirtyItems);
                ReplaceAll(failureItems, currentDiagnostics.Failures);
                ReplaceAll(historyItems, currentDiagnostics.History);
                summary.Text = BuildDiagnosticsSummary(currentDiagnostics);
                status.Text = successMessage;
            }
            catch (Exception ex)
            {
                status.Text = $"操作に失敗しました: {ex.Message}";
            }
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var clear = new Button
        {
            Content = "ログ削除",
            MinWidth = 96,
            Height = 28,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "同期ログと失敗詳細だけを削除します。未同期データは削除されません。"
        };
        var close = new Button { Content = "閉じる", MinWidth = 96, Height = 28 };
        var retryFailures = new Button { Content = "失敗分を再同期", MinWidth = 116, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsEnabled = retryFailuresAsync is not null && diagnostics.Failures.Any(item => !string.IsNullOrWhiteSpace(item.LocalId)) };
        var exportDirty = new Button { Content = "未同期CSV出力", MinWidth = 116, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        var exportLog = new Button { Content = "診断ログ出力", MinWidth = 116, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        var refreshReminders = new Button { Content = "Google通知設定を再取得", MinWidth = 150, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsEnabled = refreshGoogleRemindersAsync is not null };
        retryFailures.Click += async (_, _) =>
        {
            if (retryFailuresAsync is not null)
            {
                var ids = currentDiagnostics.Failures
                    .Select(item => item.LocalId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (ConfirmBulk(owner, "失敗分を再同期", ids.Length, "失敗診断に記録された dirty データだけを再同期します。"))
                {
                    await RunAndRefreshAsync(() => retryFailuresAsync(ids), "失敗した同期を再試行しました。");
                }
            }
        };
        refreshReminders.Click += async (_, _) =>
        {
            if (refreshGoogleRemindersAsync is not null)
            {
                await RunAndRefreshAsync(async () => await refreshGoogleRemindersAsync(), "Google通知設定を更新しました。");
            }
        };
        exportDirty.Click += (_, _) => ExportText(owner, "unsynced.csv", BuildDirtyCsv(currentDiagnostics.DirtyItems));
        exportLog.Click += (_, _) => ExportText(owner, "sync-diagnostics.txt", BuildDiagnosticsLog(currentDiagnostics));
        clear.Click += async (_, _) =>
        {
            if (ConfirmBulk(owner, "ログ削除", 1, "同期ログと失敗診断を削除します。未同期データは削除されません。"))
            {
                await RunAndRefreshAsync(clearAsync, "同期ログを削除しました。");
            }
        };
        close.Click += (_, _) => window.Close();
        buttons.Children.Add(retryFailures);
        buttons.Children.Add(refreshReminders);
        buttons.Children.Add(exportDirty);
        buttons.Children.Add(exportLog);
        buttons.Children.Add(clear);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        var tabs = new TabControl();
        var calendarGrid = new DataGrid
        {
            ItemsSource = calendarItems,
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
            ItemsSource = dirtyItems,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow
        };
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "種別", Binding = new Binding(nameof(SyncDirtyItem.Kind)), Width = 70 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "操作", Binding = new Binding(nameof(SyncDirtyItem.Operation)), Width = 70 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "カレンダー", Binding = new Binding(nameof(SyncDirtyItem.CalendarId)), Width = 120 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "開始", Binding = new Binding(nameof(SyncDirtyItem.Start)), Width = 150 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "件名", Binding = new Binding(nameof(SyncDirtyItem.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "Google ID", Binding = new Binding(nameof(SyncDirtyItem.GoogleEventId)), Width = 140 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "更新", Binding = new Binding(nameof(SyncDirtyItem.UpdatedAt)), Width = 150 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "失敗理由", Binding = new Binding(nameof(SyncDirtyItem.FailureReason)), Width = 180 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "詳細", Binding = new Binding(nameof(SyncDirtyItem.ErrorMessage)), Width = 220 });
        dirtyGrid.Columns.Add(new DataGridTextColumn { Header = "変更内容", Binding = new Binding(nameof(SyncDirtyItem.ChangeFieldsText)), Width = 160 });
        var dirtyPanel = new DockPanel();
        var dirtyButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        var openDirty = new Button { Content = "選択行を開く", MinWidth = 104, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsEnabled = openDirtyItemAsync is not null };
        var retryDirty = new Button { Content = "選択行を再同期", MinWidth = 116, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsEnabled = retryDirtyItemsAsync is not null };
        var markSynced = new Button { Content = "同期済み扱い", MinWidth = 110, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsEnabled = markDirtyItemsSyncedAsync is not null };
        var discardLocal = new Button { Content = "ローカル変更破棄", MinWidth = 120, Height = 28, IsEnabled = discardDirtyItemsAsync is not null };
        openDirty.IsEnabled = false;
        retryDirty.IsEnabled = false;
        markSynced.IsEnabled = false;
        discardLocal.IsEnabled = false;

        void UpdateDirtyActionState()
        {
            var hasSelection = GetSelectedDirtyIds(dirtyGrid).Count > 0;
            openDirty.IsEnabled = hasSelection && openDirtyItemAsync is not null;
            retryDirty.IsEnabled = hasSelection && retryDirtyItemsAsync is not null;
            markSynced.IsEnabled = hasSelection && markDirtyItemsSyncedAsync is not null;
            discardLocal.IsEnabled = hasSelection && discardDirtyItemsAsync is not null;
        }

        dirtyGrid.SelectionChanged += (_, _) => UpdateDirtyActionState();

        openDirty.Click += async (_, _) =>
        {
            if (openDirtyItemAsync is not null && GetSelectedDirtyIds(dirtyGrid).FirstOrDefault() is { } id)
            {
                await openDirtyItemAsync(id);
                window.Close();
            }
        };
        retryDirty.Click += async (_, _) =>
        {
            var ids = GetSelectedDirtyIds(dirtyGrid);
            if (retryDirtyItemsAsync is not null && ConfirmBulk(owner, "選択行を再同期", ids.Count, "選択した dirty データだけを Google へ送信します。"))
            {
                await RunAndRefreshAsync(() => retryDirtyItemsAsync(ids), "選択した未同期データを再同期しました。");
            }
        };
        markSynced.Click += async (_, _) =>
        {
            var ids = GetSelectedDirtyIds(dirtyGrid);
            if (markDirtyItemsSyncedAsync is not null && ConfirmBulk(owner, "選択行を同期済み扱い", ids.Count, "Googleへ送信せず dirty 状態を解除します。実行前に自動バックアップを作成します。"))
            {
                await RunAndRefreshAsync(() => markDirtyItemsSyncedAsync(ids), "選択した未同期データを同期済み扱いにしました。");
            }
        };
        discardLocal.Click += async (_, _) =>
        {
            var ids = GetSelectedDirtyIds(dirtyGrid);
            if (discardDirtyItemsAsync is not null && ConfirmBulk(owner, "選択行のローカル変更を破棄", ids.Count, "Googleから再取得できるものだけ復元し、ローカル新規は削除します。実行前に自動バックアップを作成します。"))
            {
                await RunAndRefreshAsync(() => discardDirtyItemsAsync(ids), "選択したローカル変更を破棄しました。");
            }
        };
        dirtyButtons.Children.Add(openDirty);
        dirtyButtons.Children.Add(retryDirty);
        dirtyButtons.Children.Add(markSynced);
        dirtyButtons.Children.Add(discardLocal);
        DockPanel.SetDock(dirtyButtons, Dock.Top);
        dirtyPanel.Children.Add(dirtyButtons);
        dirtyPanel.Children.Add(dirtyGrid);
        tabs.Items.Add(new TabItem { Header = "未同期", Content = dirtyPanel });

        var failureGrid = new DataGrid
        {
            ItemsSource = failureItems,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true
        };
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "時刻", Binding = new Binding(nameof(SyncFailureDiagnostic.OccurredAt)), Width = 150 });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "方向", Binding = new Binding(nameof(SyncFailureDiagnostic.Direction)), Width = 60 });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "操作", Binding = new Binding(nameof(SyncFailureDiagnostic.Operation)), Width = 70 });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "種別", Binding = new Binding(nameof(SyncFailureDiagnostic.Kind)), Width = 70 });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "カレンダー", Binding = new Binding(nameof(SyncFailureDiagnostic.CalendarId)), Width = 120 });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "syncToken", Binding = new Binding(nameof(SyncFailureDiagnostic.SyncTokenPresent)), Width = 80 });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "pageToken", Binding = new Binding(nameof(SyncFailureDiagnostic.PageToken)), Width = 100 });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "分類", Binding = new Binding(nameof(SyncFailureDiagnostic.FailureCategory)), Width = 110 });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "開始日時", Binding = new Binding(nameof(SyncFailureDiagnostic.Start)), Width = 150 });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "件名", Binding = new Binding(nameof(SyncFailureDiagnostic.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "Google ID", Binding = new Binding(nameof(SyncFailureDiagnostic.GoogleEventId)), Width = 130 });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "HTTP", Binding = new Binding(nameof(SyncFailureDiagnostic.HttpStatusCode)), Width = 70 });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "失敗理由", Binding = new Binding(nameof(SyncFailureDiagnostic.FailureReason)), Width = 220 });
        failureGrid.Columns.Add(new DataGridTextColumn { Header = "例外", Binding = new Binding(nameof(SyncFailureDiagnostic.ExceptionMessage)), Width = 220 });
        tabs.Items.Add(new TabItem { Header = "失敗詳細", Content = failureGrid });

        var historyGrid = new DataGrid
        {
            ItemsSource = historyItems,
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

    private static string BuildDiagnosticsSummary(SyncDiagnosticsSnapshot diagnostics)
    {
        var last = diagnostics.LastResult;
        return last is null
            ? $"未同期変更: {diagnostics.DirtyCount} 件\n最後の同期結果はありません。"
            : $"未同期変更: {diagnostics.DirtyCount} 件\n最後の同期: {last.FinishedAt:yyyy/MM/dd HH:mm:ss} / {last.SummaryText}";
    }

    private static void ReplaceAll<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static Style CreatePreviewRowStyle()
    {
        var style = new Style(typeof(DataGridRow));
        style.Triggers.Add(new DataTrigger { Binding = new Binding(nameof(SyncPreviewItem.Kind)), Value = "push", Setters = { new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.Honeydew) } });
        style.Triggers.Add(new DataTrigger { Binding = new Binding(nameof(SyncPreviewItem.Kind)), Value = "pull", Setters = { new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.AliceBlue) } });
        style.Triggers.Add(new DataTrigger { Binding = new Binding(nameof(SyncPreviewItem.Kind)), Value = "delete", Setters = { new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.MistyRose) } });
        style.Triggers.Add(new DataTrigger { Binding = new Binding(nameof(SyncPreviewItem.Kind)), Value = "remote-delete", Setters = { new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.MistyRose) } });
        style.Triggers.Add(new DataTrigger { Binding = new Binding(nameof(SyncPreviewItem.Kind)), Value = "error", Setters = { new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.LemonChiffon) } });
        return style;
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

    private static IReadOnlyList<string> GetSelectedDirtyIds(DataGrid dirtyGrid)
    {
        return dirtyGrid.SelectedItems
            .OfType<SyncDirtyItem>()
            .Select(item => item.LocalId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ConfirmBulk(Window owner, string operation, int count, string impact)
    {
        if (count <= 0)
        {
            MessageBox.Show(owner, "対象行を選択してください。", operation, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var message = $"{operation}を実行します。\n対象件数: {count} 件\n影響範囲: {impact}";
        return MessageBox.Show(owner, message, operation, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private static void ExportText(Window owner, string fileName, string content)
    {
        var dialog = new SaveFileDialog { FileName = fileName, Filter = "Text files (*.txt;*.csv)|*.txt;*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog(owner) == true)
        {
            File.WriteAllText(dialog.FileName, content, Encoding.UTF8);
        }
    }

    private static string BuildDirtyCsv(IEnumerable<SyncDirtyItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine("LocalId,Kind,Operation,CalendarId,Start,Title,GoogleEventId,UpdatedAt,ChangeFields,FailureReason,ErrorMessage");
        foreach (var item in items)
        {
            builder.AppendLine(string.Join(",", Csv(item.LocalId), Csv(item.Kind), Csv(item.Operation), Csv(item.CalendarId), Csv(item.Start.ToString("O")), Csv(item.Title), Csv(item.GoogleEventId), Csv(item.UpdatedAt.ToString("O")), Csv(item.ChangeFields), Csv(item.FailureReason), Csv(item.ErrorMessage)));
        }

        return builder.ToString();
    }

    private static string BuildDiagnosticsLog(SyncDiagnosticsSnapshot diagnostics)
    {
        var builder = new StringBuilder();
        builder.AppendLine(diagnostics.LastResult?.SummaryText ?? "No sync result.");
        builder.AppendLine($"DirtyCount={diagnostics.DirtyCount}");
        foreach (var dirty in diagnostics.DirtyItems)
        {
            builder.AppendLine($"Dirty {dirty.LocalId} {dirty.CalendarId} {dirty.Operation} fields={dirty.ChangeFields ?? "Unknown"} {dirty.Title}");
        }
        foreach (var failure in diagnostics.Failures)
        {
            builder.AppendLine($"{failure.OccurredAt:O} {failure.Direction} {failure.Operation} {failure.Kind} {failure.CalendarId} syncToken={failure.SyncTokenPresent} pageToken={failure.PageToken} category={failure.FailureCategory} {failure.LocalId} {failure.GoogleEventId} {failure.Title} {failure.FailureReason} {failure.HttpStatusCode} {failure.GoogleErrorMessage} {failure.ExceptionMessage}");
        }

        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        value ??= "";
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
