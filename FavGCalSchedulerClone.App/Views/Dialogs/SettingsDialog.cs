using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Win32;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class SettingsDialog
{
    public static Task<SettingsDialogResult?> ShowAsync(DialogUiFactory ui, SettingsDialogRequest request)
    {
        var settings = request.Settings;
        var window = ui.CreateOwnedDialog("アプリ設定", 690, 610);
        window.ResizeMode = ResizeMode.CanResize;
        var root = new DockPanel { Margin = new Thickness(10) };
        var tabs = new TabControl { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(tabs, Dock.Top);
        root.Children.Add(tabs);
        window.Content = root;

        static StackPanel Page() => new() { Margin = new Thickness(14) };
        static TabItem Tab(string header, Panel content) => new() { Header = header, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        static ComboBox Options(IEnumerable<object> items, object? selected) => new() { ItemsSource = items, SelectedItem = selected, MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 10) };

        var appPage = Page();
        var startupView = Options(Enum.GetValues<CalendarViewMode>().Cast<object>(), settings.StartupCalendarViewMode);
        var startupTodo = Options(new object[] { "未処理ToDo", "処理済みToDo" }, settings.StartupTodoTabIndex == 0 ? "未処理ToDo" : "処理済みToDo");
        var confirmDelete = new CheckBox { Content = "スケジュールを削除する際に確認ポップアップを表示する", IsChecked = settings.ConfirmBeforeDelete, Margin = new Thickness(0, 6, 0, 6) };
        var hideEditor = new CheckBox { Content = "スケジュール編集時にメインウィンドウを非表示にする", IsChecked = settings.HideMainWindowWhileEditingSchedule, Margin = new Thickness(0, 6, 0, 6) };
        var noReuse = new CheckBox { Content = "新規入力時、場所や件名に前回の入力内容を設定しない", IsChecked = !settings.ReuseLastScheduleInput, Margin = new Thickness(0, 6, 0, 6) };
        var defaultAllDay = new CheckBox { Content = "スケジュール作成時に終日にチェックを付ける", IsChecked = settings.DefaultNewEventIsAllDay, Margin = new Thickness(0, 6, 0, 6) };
        var defaultReminder = new ComboBox { ItemsSource = request.ReminderOptions, DisplayMemberPath = nameof(ReminderOption.Label), SelectedValuePath = nameof(ReminderOption.MinutesBeforeStart), SelectedValue = settings.DefaultScheduleReminderMinutes, MinWidth = 190, HorizontalAlignment = HorizontalAlignment.Left };
        appPage.Children.Add(new TextBlock { Text = "起動時のカレンダー表示タイプ" });
        appPage.Children.Add(startupView);
        appPage.Children.Add(new TextBlock { Text = "起動時のToDoタブ表示タイプ" });
        appPage.Children.Add(startupTodo);
        appPage.Children.Add(confirmDelete);
        appPage.Children.Add(hideEditor);
        appPage.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        appPage.Children.Add(noReuse);
        appPage.Children.Add(defaultAllDay);
        appPage.Children.Add(new TextBlock { Text = "新規スケジュールの通知時間の既定値", Margin = new Thickness(0, 10, 0, 4) });
        appPage.Children.Add(defaultReminder);
        tabs.Items.Add(Tab("アプリ設定", appPage));

        var displayPage = Page();
        var calendarFont = Options(new object[] { 1, 2, 3 }, settings.CalendarLabelFontSizeIndex);
        var sideFont = Options(new object[] { 1, 2, 3 }, settings.SideListFontSizeIndex);
        var weekdayType = Options(Enum.GetValues<WeekdayDisplayType>().Cast<object>(), settings.WeekdayDisplayType);
        var mondayStart = new CheckBox { Content = "カレンダーを月曜始まりにする", IsChecked = settings.WeekStartsOnMonday, Margin = new Thickness(0, 8, 0, 14) };
        var opacity = new Slider { Minimum = 64, Maximum = 255, Value = settings.WindowOpacity, TickFrequency = 1, IsSnapToTickEnabled = true };
        var opacityLabel = new TextBlock { Text = $"透明度 ({settings.WindowOpacity})" };
        opacity.ValueChanged += (_, _) => opacityLabel.Text = $"透明度 ({(int)opacity.Value})";
        foreach (var pair in new[] { ("カレンダー表示文字サイズ", calendarFont), ("一覧表示文字サイズ", sideFont), ("曜日表示タイプ", weekdayType) })
        {
            displayPage.Children.Add(new TextBlock { Text = pair.Item1 });
            displayPage.Children.Add(pair.Item2);
        }
        displayPage.Children.Add(mondayStart);
        displayPage.Children.Add(opacityLabel);
        displayPage.Children.Add(opacity);
        tabs.Items.Add(Tab("表示設定", displayPage));

        var colorPage = Page();
        var colorControls = new List<(string ColorId, TextBox Label, CheckBox IsEnabled)>();
        var colorGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        colorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddText(colorGrid, "Color", 0, 0);
        AddText(colorGrid, "Label", 0, 2);
        AddText(colorGrid, "Enabled", 0, 3);

        for (var colorId = 1; colorId <= 11; colorId++)
        {
            var id = colorId.ToString();
            var configured = settings.EventColorSettings.FirstOrDefault(item => item.ColorId == id);
            var colors = TagService.DefaultEventColorPalette.TryGetValue(id, out var paletteColor)
                ? paletteColor
                : new EventDisplayColors(TagService.DefaultDisplayColor, TagService.DefaultDisplayForegroundColor);
            var row = colorGrid.RowDefinitions.Count;
            colorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var swatch = new Border
            {
                Width = 54,
                Height = 16,
                BorderBrush = Brushes.SlateGray,
                BorderThickness = new Thickness(1),
                Background = CreateBrush(colors.Background),
                Margin = new Thickness(0, 2, 8, 6)
            };
            Grid.SetRow(swatch, row);
            Grid.SetColumn(swatch, 0);
            colorGrid.Children.Add(swatch);
            AddText(colorGrid, id, row, 1);
            var label = new TextBox
            {
                Text = configured?.Label ?? $"色 {id}",
                MinWidth = 220,
                Margin = new Thickness(0, 0, 12, 6)
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 2);
            colorGrid.Children.Add(label);
            var enabled = new CheckBox
            {
                IsChecked = configured?.IsEnabled ?? true,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(enabled, row);
            Grid.SetColumn(enabled, 3);
            colorGrid.Children.Add(enabled);
            colorControls.Add((id, label, enabled));
        }

        colorPage.Children.Add(new TextBlock { Text = "予定色の表示名と有効状態" });
        colorPage.Children.Add(colorGrid);
        tabs.Items.Add(Tab("予定色", colorPage));

        var todoPage = Page();
        var periods = new object[] { 0, 1, 3, 6, 12 };
        var incompletePeriod = Options(periods, settings.IncompleteTodoDisplayPeriodMonths);
        var completedPeriod = Options(periods, settings.CompletedTodoDisplayPeriodMonths);
        todoPage.Children.Add(new TextBlock { Text = "ToDo（未処理）の表示期間（月数、0 = 全て）" });
        todoPage.Children.Add(incompletePeriod);
        todoPage.Children.Add(new TextBlock { Text = "ToDo（処理済み）の表示期間（月数、0 = 全て）" });
        todoPage.Children.Add(completedPeriod);
        tabs.Items.Add(Tab("ToDo設定", todoPage));

        var historyPage = Page();
        var clearLocation = new Button { Content = "場所入力履歴の削除", Width = 180, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 22) };
        var clearTitle = new Button { Content = "件名入力履歴の削除", Width = 180, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 22) };
        clearLocation.Click += async (_, _) =>
        {
            if (MessageBox.Show(window, "場所入力履歴を削除しますか。", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await request.ClearLocationHistoryAsync();
            }
        };
        clearTitle.Click += async (_, _) =>
        {
            if (MessageBox.Show(window, "件名入力履歴を削除しますか。", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await request.ClearTitleHistoryAsync();
            }
        };
        historyPage.Children.Add(new TextBlock { Text = "場所入力履歴" });
        historyPage.Children.Add(clearLocation);
        historyPage.Children.Add(new TextBlock { Text = "件名入力履歴" });
        historyPage.Children.Add(clearTitle);
        tabs.Items.Add(Tab("履歴設定", historyPage));

        var notifyPage = Page();
        var soundEnabled = new CheckBox { Content = "通知時に音声ファイルを再生する", IsChecked = settings.EnableReminderSound };
        var soundPath = new TextBox { Text = settings.ReminderSoundFilePath ?? "", MinWidth = 430, Margin = new Thickness(0, 8, 0, 8) };
        var browseSound = new Button { Content = "参照", Width = 80 };
        browseSound.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "音声ファイル|*.wav;*.mp3;*.wma|すべてのファイル|*.*" };
            if (dialog.ShowDialog(window) == true) soundPath.Text = dialog.FileName;
        };
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = settings.ReminderSoundVolume, Width = 360, IsSnapToTickEnabled = true };
        var testSound = new Button { Content = "テスト再生", Width = 100 };
        var stopSound = new Button { Content = "停止", Width = 80 };
        var testNotification = new Button { Content = "通知テスト", Width = 110 };
        testSound.Click += (_, _) => request.PlayPreviewSound(soundPath.Text, (int)volume.Value);
        stopSound.Click += (_, _) => request.StopPreviewSound();
        testNotification.Click += async (_, _) =>
        {
            var success = await request.ShowTestNotificationAsync();
            MessageBox.Show(
                window,
                success ? "通知テストを表示しました。" : "通知テストに失敗しました。通知一覧を確認してください。",
                "通知テスト",
                MessageBoxButton.OK,
                success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        };
        var toast = new CheckBox { Content = "Windowsトースト通知を使う", IsChecked = settings.UseWindowsToastNotifications, Margin = new Thickness(0, 18, 0, 0) };
        notifyPage.Children.Add(soundEnabled);
        notifyPage.Children.Add(soundPath);
        notifyPage.Children.Add(browseSound);
        notifyPage.Children.Add(new TextBlock { Text = "再生音量", Margin = new Thickness(0, 12, 0, 4) });
        notifyPage.Children.Add(volume);
        var soundButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        soundButtons.Children.Add(testSound);
        soundButtons.Children.Add(stopSound);
        soundButtons.Children.Add(testNotification);
        notifyPage.Children.Add(soundButtons);
        notifyPage.Children.Add(toast);
        tabs.Items.Add(Tab("通知設定", notifyPage));

        var accountPage = Page();
        var oauthPath = new TextBox { Text = request.OAuthClientJsonPath, MinWidth = 500, Margin = new Thickness(0, 8, 0, 12) };
        var chooseOAuth = new Button { Content = "OAuth JSONを選択", Width = 140 };
        var authorize = new Button { Content = "Google認証", Width = 120 };
        var clearToken = new Button { Content = "トークン削除", Width = 120 };
        var reloadCalendars = new Button { Content = "カレンダー一覧を更新", Width = 170 };
        chooseOAuth.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Google OAuth client JSON (*.json)|*.json|All files (*.*)|*.*" };
            if (dialog.ShowDialog(window) == true) oauthPath.Text = dialog.FileName;
        };
        authorize.Click += async (_, _) => await RunGoogleOperationAsync(window, "Google認証", "Google認証が完了しました。", "Google認証エラー", async () =>
        {
            await request.SetOAuthClientJsonPathAsync(oauthPath.Text);
            await request.AuthorizeGoogleAsync();
        });
        clearToken.Click += async (_, _) => await RunGoogleOperationAsync(window, "トークン削除", "保存済みGoogleトークンを削除しました。", "トークン削除エラー", request.ClearTokensAsync);
        reloadCalendars.Click += async (_, _) => await RunGoogleOperationAsync(window, "カレンダー一覧", "カレンダー一覧を更新しました。", "カレンダー一覧更新エラー", async () =>
        {
            await request.SetOAuthClientJsonPathAsync(oauthPath.Text);
            await request.ReloadAvailableCalendarsAsync();
        });
        accountPage.Children.Add(new TextBlock { Text = "Google Calendar API OAuth client JSON" });
        accountPage.Children.Add(oauthPath);
        foreach (var button in new[] { chooseOAuth, authorize, clearToken, reloadCalendars }) accountPage.Children.Add(button);
        tabs.Items.Add(Tab("GoogleAccount設定", accountPage));

        var syncPage = Page();
        var syncPreview = new CheckBox { Content = "手動同期前にプレビューを表示する", IsChecked = settings.ShowSyncPreviewBeforeManualSync, Margin = new Thickness(0, 8, 0, 8) };
        var syncDiagnostics = new CheckBox { Content = "同期診断ログを保存する", IsChecked = settings.EnableSyncDiagnostics, Margin = new Thickness(0, 0, 0, 12) };
        var conflictPolicy = Options(Enum.GetValues<SyncConflictPolicy>().Cast<object>(), settings.SyncConflictPolicy);
        var syncAfterChange = new CheckBox { Content = "スケジュールの追加／編集／削除時にGoogleカレンダーと同期を行う", IsChecked = settings.SyncAfterLocalChange, Margin = new Thickness(0, 8, 0, 18) };
        var syncInterval = Options(new object[] { "自動同期しない", "30分", "1時間", "2時間", "6時間" }, settings.AutomaticSyncIntervalMinutes switch { 30 => "30分", 60 => "1時間", 120 => "2時間", 360 => "6時間", _ => "自動同期しない" });
        syncPage.Children.Add(syncAfterChange);
        syncPage.Children.Add(syncPreview);
        syncPage.Children.Add(syncDiagnostics);
        syncPage.Children.Add(new TextBlock { Text = "競合時の扱い" });
        syncPage.Children.Add(conflictPolicy);
        syncPage.Children.Add(new TextBlock { Text = "スケジュール表示中の自動同期間隔" });
        syncPage.Children.Add(syncInterval);
        tabs.Items.Add(Tab("Googleカレンダー設定", syncPage));

        var buttons = ui.DialogButtons(window, "OK", "キャンセル");
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Remove(tabs);
        root.Children.Add(tabs);
        if (window.ShowDialog() != true)
        {
            request.StopPreviewSound();
            return Task.FromResult<SettingsDialogResult?>(null);
        }

        request.StopPreviewSound();
        settings.StartupCalendarViewMode = startupView.SelectedItem is CalendarViewMode mode ? mode : CalendarViewMode.Month;
        settings.StartupTodoTabIndex = startupTodo.SelectedIndex;
        settings.ConfirmBeforeDelete = confirmDelete.IsChecked == true;
        settings.CloseButtonExitsApplication = false;
        settings.HideMainWindowWhileEditingSchedule = hideEditor.IsChecked == true;
        settings.ReuseLastScheduleInput = noReuse.IsChecked != true;
        settings.DefaultNewEventIsAllDay = defaultAllDay.IsChecked == true;
        settings.DefaultScheduleReminderMinutes = defaultReminder.SelectedValue as int?;
        settings.CalendarLabelFontSizeIndex = calendarFont.SelectedItem is int cf ? cf : 2;
        settings.SideListFontSizeIndex = sideFont.SelectedItem is int sf ? sf : 2;
        settings.WeekdayDisplayType = weekdayType.SelectedItem is WeekdayDisplayType weekday ? weekday : WeekdayDisplayType.EnglishShort;
        settings.WeekStartsOnMonday = mondayStart.IsChecked == true;
        settings.WindowOpacity = (int)opacity.Value;
        settings.IncompleteTodoDisplayPeriodMonths = incompletePeriod.SelectedItem is int incomplete ? incomplete : 0;
        settings.CompletedTodoDisplayPeriodMonths = completedPeriod.SelectedItem is int completed ? completed : 0;
        settings.EnableReminderSound = soundEnabled.IsChecked == true;
        settings.ReminderSoundFilePath = string.IsNullOrWhiteSpace(soundPath.Text) ? null : soundPath.Text.Trim();
        settings.ReminderSoundVolume = (int)volume.Value;
        settings.UseWindowsToastNotifications = toast.IsChecked == true;
        settings.OAuthClientJsonPath = string.IsNullOrWhiteSpace(oauthPath.Text) ? null : oauthPath.Text.Trim();
        settings.SyncAfterLocalChange = syncAfterChange.IsChecked == true;
        settings.ShowSyncPreviewBeforeManualSync = syncPreview.IsChecked == true;
        settings.EnableSyncDiagnostics = syncDiagnostics.IsChecked == true;
        settings.SyncConflictPolicy = conflictPolicy.SelectedItem is SyncConflictPolicy policy ? policy : SyncConflictPolicy.SkipLocalDirty;
        settings.AutomaticSyncIntervalMinutes = syncInterval.SelectedIndex switch { 1 => 30, 2 => 60, 3 => 120, 4 => 360, _ => null };
        settings.EventColorSettings = colorControls
            .Select(item => new EventColorSetting
            {
                ColorId = item.ColorId,
                Label = string.IsNullOrWhiteSpace(item.Label.Text) ? null : item.Label.Text.Trim(),
                IsEnabled = item.IsEnabled.IsChecked == true
            })
            .ToList();
        return Task.FromResult<SettingsDialogResult?>(new SettingsDialogResult(settings, oauthPath.Text));
    }

    private static void AddText(Grid grid, string text, int row, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 0, 8, 6),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static Brush CreateBrush(string color)
    {
        try
        {
            return (Brush)new BrushConverter().ConvertFromString(color)!;
        }
        catch
        {
            return Brushes.White;
        }
    }

    private static async Task RunGoogleOperationAsync(Window owner, string title, string successMessage, string errorTitle, Func<Task> operation)
    {
        try
        {
            await operation();
            MessageBox.Show(owner, successMessage, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

internal sealed record SettingsDialogRequest(
    AppSettings Settings,
    string OAuthClientJsonPath,
    IEnumerable<ReminderOption> ReminderOptions,
    Func<Task> ClearLocationHistoryAsync,
    Func<Task> ClearTitleHistoryAsync,
    Action<string, int> PlayPreviewSound,
    Action StopPreviewSound,
    Func<string, Task> SetOAuthClientJsonPathAsync,
    Func<Task> AuthorizeGoogleAsync,
    Func<Task> ClearTokensAsync,
    Func<Task> ReloadAvailableCalendarsAsync,
    Func<Task<bool>> ShowTestNotificationAsync);

internal sealed record SettingsDialogResult(AppSettings Settings, string OAuthClientJsonPath);
