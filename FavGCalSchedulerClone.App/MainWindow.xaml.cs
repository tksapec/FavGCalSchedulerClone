using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;

namespace FavGCalSchedulerClone.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ReminderNotificationService _reminderService;
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();
        var repository = new CalendarRepository();
        _viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        _reminderService = new ReminderNotificationService(repository);
        DataContext = _viewModel;
        ToastNotificationManagerCompat.OnActivated += ToastNotificationManagerCompat_OnActivated;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
            _reminderService.SetNotifier(CreateReminderNotifier());
            await _reminderService.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested || _viewModel.CloseButtonExitsApplication)
        {
            _reminderService.Stop();
            _reminderService.Dispose();
            ToastNotificationManagerCompat.OnActivated -= ToastNotificationManagerCompat_OnActivated;
            return;
        }

        e.Cancel = true;
        WindowState = WindowState.Minimized;
    }

    private IReminderNotifier CreateReminderNotifier()
    {
        var fallback = new MessageBoxReminderNotifier(this);
        return _viewModel.UseWindowsToastNotifications
            ? new FallbackReminderNotifier(new WindowsToastReminderNotifier(), fallback)
            : fallback;
    }

    private async void ToastNotificationManagerCompat_OnActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        var arguments = ToastArguments.Parse(args.Argument);
        if (!arguments.TryGetValue("action", out var action)
            || !arguments.TryGetValue("occurrenceKey", out var occurrenceKey))
        {
            return;
        }

        if (string.Equals(action, "snooze", StringComparison.OrdinalIgnoreCase)
            && arguments.TryGetValue("minutes", out var minutesValue)
            && int.TryParse(minutesValue, out var minutes))
        {
            await _reminderService.SnoozeAsync(occurrenceKey, minutes);
            return;
        }

        if (string.Equals(action, "open", StringComparison.OrdinalIgnoreCase))
        {
            var history = await _reminderService.LoadHistoryAsync();
            var item = history.FirstOrDefault(entry => string.Equals(entry.OccurrenceKey, occurrenceKey, StringComparison.Ordinal));
            if (item is not null)
            {
                await Dispatcher.InvokeAsync(async () => await OpenReminderHistoryItemAsync(item));
            }
        }
    }

    private async void DayList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        await ShowScheduleDialogAsync();
    }

    private async void AddScheduleMenu_Click(object sender, RoutedEventArgs e)
    {
        await ShowScheduleDialogAsync();
    }

    private async void AddTodoMenu_Click(object sender, RoutedEventArgs e)
    {
        await ShowTodoDialogAsync();
    }

    private async void BackupAllCalendarsMenu_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "FavGCalSchedulerClone backup (*.zip)|*.zip|All files (*.*)|*.*",
            FileName = _viewModel.DefaultBackupFileName,
            Title = "バックアップ先を選択"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = await _viewModel.BackupAllCalendarsAsync(dialog.FileName);
            MessageBox.Show(this, $"バックアップを作成しました。\n\n{result.BackupPath}", "バックアップ完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "バックアップ失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RestoreAllCalendarsMenu_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "FavGCalSchedulerClone backup (*.zip)|*.zip|All files (*.*)|*.*",
            Title = "リストアするバックアップを選択"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            "現在の calendar.db を退避してから、バックアップ内容で上書きします。続行しますか。",
            "リストア確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var result = await _viewModel.RestoreAllCalendarsAsync(dialog.FileName);
            var previousDatabase = string.IsNullOrWhiteSpace(result.PreviousDatabaseBackupPath)
                ? string.Empty
                : $"\n\n退避したDB:\n{result.PreviousDatabaseBackupPath}";
            MessageBox.Show(this, $"リストアが完了しました。{previousDatabase}", "リストア完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "リストア失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportCsvMenu_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "インポートするCSVを選択"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = await _viewModel.ImportCsvAsync(dialog.FileName);
            if (result.Errors.Count == 0)
            {
                MessageBox.Show(this, $"{result.Events.Count} 件を取り込みました。", "インポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBox.Show(
                this,
                $"{result.Events.Count} 件を取り込みました。\n\nエラーがある行:\n{FormatImportErrors(result.Errors)}",
                "インポート完了",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "インポート失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportFavGCalSchedulerMenu_Click(object sender, RoutedEventArgs e)
    {
        await ShowFavGCalSchedulerImportDialogAsync();
    }

    private async void ExportCsvMenu_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"FavGCalSchedulerClone-{_viewModel.CurrentMonth.Year}.csv",
            Title = "エクスポート先を選択"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = await _viewModel.ExportCurrentYearCsvAsync(dialog.FileName);
            MessageBox.Show(this, $"{result.ExportedCount} 件をCSVへ出力しました。\n\n{result.CsvPath}", "エクスポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エクスポート失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PrintPreviewMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var plan = await _viewModel.CreateMonthlyPrintPlanAsync();
            ShowPrintPreview(plan);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "印刷プレビュー失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PrintMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var plan = await _viewModel.CreateMonthlyPrintPlanAsync();
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var width = dialog.PrintableAreaWidth > 0 ? dialog.PrintableAreaWidth : 1122;
            var height = dialog.PrintableAreaHeight > 0 ? dialog.PrintableAreaHeight : 794;
            var visual = MonthlyPrintVisualBuilder.Build(plan, width, height);
            visual.Measure(new Size(width, height));
            visual.Arrange(new Rect(0, 0, width, height));
            dialog.PrintVisual(visual, plan.Title);
            _viewModel.Status = "印刷を開始しました。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "印刷失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ScheduleListMenu_Click(object sender, RoutedEventArgs e)
    {
        await ShowEventListDialogAsync("スケジュール一覧", string.Empty);
    }

    private async void SearchMenu_Click(object sender, RoutedEventArgs e)
    {
        await ShowSearchDialogAsync();
    }

    private void SettingsMenu_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsDialog();
    }

    private async void ReminderHistoryMenu_Click(object sender, RoutedEventArgs e)
    {
        await ShowReminderHistoryDialogAsync();
    }

    private async void DeleteEventButton_Click(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedEventWithOptionalConfirmationAsync();
    }

    private async void SelectedDayEventsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedEvent is null)
        {
            return;
        }

        await ShowScheduleDialogAsync();
    }

    private async void CalendarSelectionMenu_Checked(object sender, RoutedEventArgs e)
    {
        await _viewModel.ApplyCalendarSelectionAsync();
    }

    private async void CalendarSelectionMenu_Unchecked(object sender, RoutedEventArgs e)
    {
        await _viewModel.ApplyCalendarSelectionAsync();
    }

    private void AboutMenu_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "FavGCalSchedulerClone\nVersion 0.1.0",
            "バージョン情報",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e)
    {
        _exitRequested = true;
        Close();
    }

    private void ShowPrintPreview(MonthlyPrintPlan plan)
    {
        var window = CreateOwnedDialog("印刷プレビュー", 1180, 860);
        window.ResizeMode = ResizeMode.CanResize;
        window.MinWidth = 900;
        window.MinHeight = 640;

        var preview = MonthlyPrintVisualBuilder.Build(plan, 1122, 794);
        preview.Margin = new Thickness(16);

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = System.Windows.Media.Brushes.WhiteSmoke,
            Content = new Border
            {
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(16),
                Child = preview
            }
        };

        window.Content = scrollViewer;
        window.ShowDialog();
    }

    private async Task ShowScheduleDialogAsync()
    {
        var date = _viewModel.SelectedDay?.Date ?? DateTime.Today;
        var editingEvent = _viewModel.SelectedEvent;
        var window = CreateOwnedDialog(editingEvent is null ? "スケジュール追加" : "スケジュール編集", 640, 520);
        var root = CreateDialogRoot();
        window.Content = root;

        var startDate = new DatePicker { SelectedDate = editingEvent?.Start.Date ?? date };
        var endDate = new DatePicker
        {
            SelectedDate = editingEvent is null
                ? date
                : (editingEvent.IsAllDay ? editingEvent.End.Date.AddDays(-1) : editingEvent.End.Date)
        };
        var startTime = TimeComboBox(editingEvent?.Start.ToString("HH:mm") ?? "09:00");
        var endTime = TimeComboBox(editingEvent?.End.ToString("HH:mm") ?? "10:00");
        var isAllDay = new CheckBox
        {
            Content = "終日",
            IsChecked = editingEvent?.IsAllDay ?? _viewModel.DefaultNewEventIsAllDay,
            VerticalAlignment = VerticalAlignment.Center
        };
        var reminder = new ComboBox
        {
            ItemsSource = _viewModel.ReminderOptions,
            DisplayMemberPath = nameof(ReminderOption.Label),
            SelectedValuePath = nameof(ReminderOption.MinutesBeforeStart),
            SelectedValue = editingEvent?.ReminderMinutesBeforeStart
        };
        var location = new TextBox { Text = editingEvent?.Location ?? string.Empty };
        var calendar = new ComboBox
        {
            ItemsSource = _viewModel.AvailableCalendars,
            DisplayMemberPath = nameof(GoogleCalendarSelectionItem.Summary),
            SelectedValuePath = nameof(GoogleCalendarSelectionItem.Id),
            SelectedValue = editingEvent?.CalendarId ?? _viewModel.EditorCalendarId
        };
        var title = new TextBox { Text = editingEvent?.Title ?? string.Empty };
        var description = new TextBox
        {
            Text = editingEvent?.Description ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 96,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        root.Children.Add(SectionHeader("日時"));
        root.Children.Add(FormGrid(
            ("開始日", startDate, "終了日", endDate),
            ("開始時刻", startTime, "終了時刻", endTime),
            ("", isAllDay, "通知", reminder),
            ("保存先カレンダー", calendar, "", new TextBlock())));
        root.Children.Add(SectionHeader("詳細"));
        root.Children.Add(WideField("件名", title));
        root.Children.Add(WideField("場所", location));
        root.Children.Add(WideField("内容", description));
        root.Children.Add(DialogButtons(window, "保存", "キャンセル"));

        if (window.ShowDialog() != true)
        {
            return;
        }

        _viewModel.EditorCalendarId = calendar.SelectedValue?.ToString() ?? _viewModel.EditorCalendarId;
        if (editingEvent is null)
        {
            _viewModel.BeginNewEvent(startDate.SelectedDate ?? date);
        }

        _viewModel.StartDate = startDate.SelectedDate ?? date;
        _viewModel.EndDate = endDate.SelectedDate ?? _viewModel.StartDate;
        _viewModel.StartTime = startTime.Text;
        _viewModel.EndTime = endTime.Text;
        _viewModel.IsAllDay = isAllDay.IsChecked == true;
        _viewModel.ReminderMinutesBeforeStart = reminder.SelectedValue as int?;
        _viewModel.Location = location.Text;
        _viewModel.Title = title.Text;
        _viewModel.Description = description.Text;
        RecurrenceEditScope? recurrenceScope = null;
        if (editingEvent?.IsRecurringSeriesItem == true)
        {
            recurrenceScope = PromptRecurrenceScope(false);
            if (recurrenceScope is null)
            {
                return;
            }
        }

        await _viewModel.SaveCurrentEventAsync(recurrenceScope);
    }

    private async Task ShowTodoDialogAsync()
    {
        var date = _viewModel.SelectedDay?.Date ?? DateTime.Today;
        var window = CreateOwnedDialog("ToDo追加", 560, 430);
        var root = CreateDialogRoot();
        window.Content = root;

        var dueDate = new DatePicker { SelectedDate = date };
        var priority = new ComboBox { SelectedIndex = 0, ItemsSource = new[] { "A", "B", "C", "D", "E", "F" } };
        var progress = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = true, Width = 210 };
        var progressLabel = new TextBlock { Text = "進捗 0%", VerticalAlignment = VerticalAlignment.Center };
        progress.ValueChanged += (_, _) => progressLabel.Text = $"進捗 {(int)progress.Value}%";
        var reminder = new ComboBox
        {
            ItemsSource = _viewModel.ReminderOptions,
            DisplayMemberPath = nameof(ReminderOption.Label),
            SelectedValuePath = nameof(ReminderOption.MinutesBeforeStart)
        };
        var calendar = new ComboBox
        {
            ItemsSource = _viewModel.AvailableCalendars,
            DisplayMemberPath = nameof(GoogleCalendarSelectionItem.Summary),
            SelectedValuePath = nameof(GoogleCalendarSelectionItem.Id),
            SelectedValue = _viewModel.EditorCalendarId
        };
        var title = new TextBox();
        var description = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        root.Children.Add(SectionHeader("期限と進捗"));
        root.Children.Add(FormGrid(
            ("期限", dueDate, "優先度", priority),
            ("進捗", progress, "", progressLabel),
            ("通知", reminder, "", new TextBlock())));
        root.Children.Add(SectionHeader("詳細"));
        root.Children.Add(WideField("件名", title));
        root.Children.Add(WideField("内容", description));
        root.Children.Add(SectionHeader("保存先"));
        root.Children.Add(FormGrid(("カレンダー", calendar, "", new TextBlock())));
        root.Children.Add(DialogButtons(window, "保存", "キャンセル"));

        if (window.ShowDialog() != true)
        {
            return;
        }

        _viewModel.EditorCalendarId = calendar.SelectedValue?.ToString() ?? _viewModel.EditorCalendarId;
        _viewModel.ReminderMinutesBeforeStart = reminder.SelectedValue as int?;
        await _viewModel.SaveTodoAsync(
            dueDate.SelectedDate ?? date,
            priority.SelectedItem?.ToString() ?? "A",
            (int)progress.Value,
            title.Text,
            description.Text);
    }

    private async Task ShowFavGCalSchedulerImportDialogAsync()
    {
        var window = CreateOwnedDialog("FavGCalSchedulerデータ移行", 720, 560);
        window.ResizeMode = ResizeMode.CanResize;
        var root = CreateDialogRoot();
        window.Content = root;

        var sourceFolder = new TextBox { Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FavGCalScheduler") };
        var oauthPath = new TextBox { Text = _viewModel.OAuthClientJsonPath };
        var comparisonZip = new TextBox { Text = "" };
        var targetCalendar = new ComboBox
        {
            ItemsSource = _viewModel.AvailableCalendars,
            DisplayMemberPath = nameof(GoogleCalendarSelectionItem.Summary),
            SelectedValuePath = nameof(GoogleCalendarSelectionItem.Id),
            SelectedValue = _viewModel.EditorCalendarId
        };
        var importSettings = new CheckBox { Content = "旧アプリ設定の一部を反映する", IsChecked = true };
        var skipDuplicates = new CheckBox { Content = "重複予定をスキップする", IsChecked = true };
        var verifyGoogle = new CheckBox { Content = "取り込み前にGoogle予定を取得して照合する", IsChecked = true };
        var analysisText = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 150,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        FavGCalImportAnalysis? analysis = null;

        var oauthButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var browseOAuth = new Button { Content = "OAuth JSON選択", MinWidth = 120 };
        var authorize = new Button { Content = "Google認証", MinWidth = 110 };
        oauthButtons.Children.Add(browseOAuth);
        oauthButtons.Children.Add(authorize);

        browseOAuth.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Google OAuth client JSON (*.json)|*.json|All files (*.*)|*.*",
                Title = "デバッグ用 OAuth client JSON を選択"
            };
            if (dialog.ShowDialog(this) == true)
            {
                oauthPath.Text = dialog.FileName;
                await _viewModel.SetOAuthClientJsonPathAsync(dialog.FileName);
            }
        };
        authorize.Click += async (_, _) =>
        {
            await _viewModel.SetOAuthClientJsonPathAsync(oauthPath.Text);
            await _viewModel.AuthorizeGoogleAsync();
            targetCalendar.ItemsSource = _viewModel.AvailableCalendars;
            analysisText.Text = "Google認証とカレンダー一覧取得が完了しました。";
        };

        var analyze = new Button { Content = "解析", MinWidth = 96 };
        analyze.Click += async (_, _) =>
        {
            analysis = await _viewModel.AnalyzeFavGCalSchedulerImportAsync(sourceFolder.Text);
            analysisText.Text = FormatFavGCalAnalysis(analysis);
        };

        root.Children.Add(SectionHeader("移行元"));
        root.Children.Add(WideField("FavGCalSchedulerフォルダ", sourceFolder));
        root.Children.Add(SectionHeader("デバッグ用 Google 連携"));
        root.Children.Add(WideField("OAuth client JSON", oauthPath));
        root.Children.Add(oauthButtons);
        root.Children.Add(SectionHeader("照合"));
        root.Children.Add(WideField("Google エクスポート ZIP", comparisonZip));
        root.Children.Add(SectionHeader("取り込み"));
        root.Children.Add(FormGrid(("既定の取り込み先", targetCalendar, "", analyze)));
        root.Children.Add(importSettings);
        root.Children.Add(skipDuplicates);
        root.Children.Add(verifyGoogle);
        root.Children.Add(WideField("解析結果", analysisText));
        root.Children.Add(DialogButtons(window, "取り込み", "キャンセル"));

        if (window.ShowDialog() != true)
        {
            return;
        }

        await _viewModel.SetOAuthClientJsonPathAsync(oauthPath.Text);
        analysis ??= await _viewModel.AnalyzeFavGCalSchedulerImportAsync(sourceFolder.Text);
        var defaultTarget = targetCalendar.SelectedValue?.ToString() ?? _viewModel.EditorCalendarId;
        var mappings = analysis.Calendars.ToDictionary(calendar => calendar.CalendarKey, _ => defaultTarget);
        var result = await _viewModel.ImportFavGCalSchedulerAsync(new FavGCalImportOptions(
            sourceFolder.Text,
            mappings,
            ImportSettings: importSettings.IsChecked == true,
            SkipDuplicates: skipDuplicates.IsChecked == true,
            VerifyGoogleEventsBeforeImport: verifyGoogle.IsChecked == true,
            MarkImportedEventsDirty: true,
            DefaultTargetCalendarId: defaultTarget,
            ComparisonZipPath: string.IsNullOrWhiteSpace(comparisonZip.Text) ? null : comparisonZip.Text));

        var comparisonText = result.ComparisonSummary is null
            ? ""
            : $"\n\n照合結果\n一致: {result.ComparisonSummary.MatchedCount} 件\n本アプリのみ: {result.ComparisonSummary.LocalOnlyCount} 件\nGoogleエクスポートのみ: {result.ComparisonSummary.ExportOnlyCount} 件";

        MessageBox.Show(
            this,
            $"取り込みが完了しました。\n\n追加: {result.ImportedCount} 件\n既存紐付け: {result.LinkedExistingGoogleCount} 件\n重複スキップ: {result.SkippedDuplicateCount} 件\n解析エラー: {result.ParseErrorCount} 件{comparisonText}",
            "FavGCalSchedulerデータ移行",
            MessageBoxButton.OK,
            result.ParseErrorCount == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task ShowSearchDialogAsync()
    {
        var window = CreateOwnedDialog("スケジュール検索", 520, 320);
        var root = CreateDialogRoot();
        window.Content = root;

        var start = new DatePicker { SelectedDate = new DateTime(_viewModel.CurrentMonth.Year, 1, 1), IsEnabled = false };
        var end = new DatePicker { SelectedDate = new DateTime(_viewModel.CurrentMonth.Year, 12, 31), IsEnabled = false };
        var query = new TextBox();

        root.Children.Add(SectionHeader("検索範囲"));
        root.Children.Add(FormGrid(("開始", start, "終了", end)));
        root.Children.Add(SectionHeader("条件"));
        root.Children.Add(WideField("検索文字列", query));
        root.Children.Add(DialogButtons(window, "検索", "キャンセル"));

        if (window.ShowDialog() == true)
        {
            await ShowEventListDialogAsync("スケジュール一覧", query.Text);
        }
    }

    private async Task ShowEventListDialogAsync(string title, string query)
    {
        var events = string.IsNullOrWhiteSpace(query)
            ? await _viewModel.LoadYearEventsAsync(_viewModel.CurrentMonth)
            : await _viewModel.SearchYearEventsAsync(_viewModel.CurrentMonth, query);

        var window = CreateOwnedDialog(title, 840, 540);
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
            ItemsSource = new ObservableCollection<CalendarEvent>(events),
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 24
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "日時", Binding = new Binding(nameof(CalendarEvent.DateDisplayText)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "カレンダー", Binding = new Binding(nameof(CalendarEvent.CalendarId)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "件名", Binding = new Binding(nameof(CalendarEvent.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        panel.Children.Add(grid);

        window.ShowDialog();
    }

    private async Task ShowReminderHistoryDialogAsync()
    {
        var history = await _reminderService.LoadHistoryAsync();
        var window = CreateOwnedDialog("通知一覧", 760, 460);
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
                await OpenReminderHistoryItemAsync(item);
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

    private async Task OpenReminderHistoryItemAsync(ReminderHistoryItem item)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        await _viewModel.SelectReminderEventAsync(item.EventId, item.OccurrenceStart);
    }

    private async void ShowSettingsDialog()
    {
        var window = CreateOwnedDialog("アプリ設定", 560, 320);
        var root = CreateDialogRoot();
        window.Content = root;

        var confirmBeforeDelete = new CheckBox
        {
            Content = "削除前に確認ダイアログを表示する",
            IsChecked = _viewModel.ConfirmBeforeDelete
        };
        var closeButtonExits = new CheckBox
        {
            Content = "閉じるボタンでアプリケーションを終了する",
            IsChecked = _viewModel.CloseButtonExitsApplication
        };
        var defaultAllDay = new CheckBox
        {
            Content = "新規予定を既定で終日にする",
            IsChecked = _viewModel.DefaultNewEventIsAllDay
        };
        var useWindowsToast = new CheckBox
        {
            Content = "Windowsトースト通知を使う",
            IsChecked = _viewModel.UseWindowsToastNotifications
        };

        root.Children.Add(SectionHeader("動作"));
        root.Children.Add(confirmBeforeDelete);
        root.Children.Add(closeButtonExits);
        root.Children.Add(defaultAllDay);
        root.Children.Add(useWindowsToast);
        root.Children.Add(DialogButtons(window, "保存", "キャンセル"));

        if (window.ShowDialog() != true)
        {
            return;
        }

        await _viewModel.SaveApplicationSettingsAsync(
            _viewModel.StartupTabIndex,
            confirmBeforeDelete.IsChecked == true,
            closeButtonExits.IsChecked == true,
            defaultAllDay.IsChecked == true,
            useWindowsToast.IsChecked == true);
        _reminderService.SetNotifier(CreateReminderNotifier());
    }

    private async Task DeleteSelectedEventWithOptionalConfirmationAsync()
    {
        if (_viewModel.SelectedEvent is null)
        {
            return;
        }

        RecurrenceEditScope? recurrenceScope = null;
        if (_viewModel.SelectedEvent.IsRecurringSeriesItem)
        {
            recurrenceScope = PromptRecurrenceScope(true);
            if (recurrenceScope is null)
            {
                return;
            }
        }

        if (_viewModel.ConfirmBeforeDelete)
        {
            var targetText = recurrenceScope switch
            {
                RecurrenceEditScope.ThisOccurrence => "この予定",
                RecurrenceEditScope.ThisAndFollowing => "この予定以降",
                RecurrenceEditScope.AllEvents => "すべての予定",
                _ => $"「{_viewModel.SelectedEvent.Title}」"
            };
            var result = MessageBox.Show(
                this,
                $"{targetText}を削除しますか。",
                "削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        await _viewModel.DeleteSelectedEventAsync(recurrenceScope);
    }

    private RecurrenceEditScope? PromptRecurrenceScope(bool isDelete)
    {
        var window = CreateOwnedDialog(isDelete ? "削除対象" : "編集対象", 420, 220);
        var root = CreateDialogRoot();
        window.Content = root;

        root.Children.Add(SectionHeader(isDelete ? "どこまで削除するか選択してください。" : "どこまで反映するか選択してください。"));

        RecurrenceEditScope? selected = null;
        root.Children.Add(CreateScopeButton(window, "この予定のみ", RecurrenceEditScope.ThisOccurrence, value => selected = value));
        root.Children.Add(CreateScopeButton(window, "この予定以降", RecurrenceEditScope.ThisAndFollowing, value => selected = value));
        root.Children.Add(CreateScopeButton(window, "すべての予定", RecurrenceEditScope.AllEvents, value => selected = value));

        var cancel = new Button { Content = "キャンセル", MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        cancel.Click += (_, _) => window.DialogResult = false;
        root.Children.Add(cancel);

        return window.ShowDialog() == true ? selected : null;
    }

    private static Button CreateScopeButton(Window window, string text, RecurrenceEditScope scope, Action<RecurrenceEditScope> setSelected)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 8),
            Height = 34
        };
        button.Click += (_, _) =>
        {
            setSelected(scope);
            window.DialogResult = true;
        };
        return button;
    }

    private static string FormatImportErrors(IReadOnlyList<CalendarCsvImportError> errors)
    {
        return string.Join(Environment.NewLine, errors.Select(error => $"行 {error.RowNumber}: {error.Message}"));
    }

    private static string FormatFavGCalAnalysis(FavGCalImportAnalysis analysis)
    {
        var lines = new List<string>
        {
            $"移行元: {analysis.SourceFolder}",
            $"対象カレンダー: {analysis.Calendars.Count} 件",
            $"検出予定: {analysis.TotalEventCount} 件",
            $"解析エラー: {analysis.ParseErrorCount} 件",
            ""
        };
        lines.AddRange(analysis.Calendars.Select(calendar =>
            $"{Path.GetFileName(calendar.SourcePath)} / {calendar.DisplayName} / {calendar.EventCount} 件 / 旧ID: {calendar.CalendarKey}"));
        if (analysis.Warnings.Count > 0)
        {
            lines.Add("");
            lines.AddRange(analysis.Warnings);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static ComboBox TimeComboBox(string selected)
    {
        return new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = true,
            Text = selected,
            ItemsSource = TimeChoices().ToArray()
        };
    }

    private static IEnumerable<string> TimeChoices()
    {
        for (var hour = 0; hour < 24; hour++)
        {
            for (var minute = 0; minute < 60; minute += 30)
            {
                yield return $"{hour:00}:{minute:00}";
            }
        }
    }

    private Window CreateOwnedDialog(string title, double width, double height)
    {
        return new Window
        {
            Owner = this,
            Title = title,
            Width = width,
            Height = height,
            MinWidth = width,
            MinHeight = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White
        };
    }

    private static StackPanel CreateDialogRoot()
    {
        return new StackPanel
        {
            Margin = new Thickness(16),
            Orientation = Orientation.Vertical
        };
    }

    private static TextBlock SectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private static Grid FormGrid(params (string LeftLabel, FrameworkElement LeftInput, string RightLabel, FrameworkElement RightInput)[] rows)
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

    private static FrameworkElement WideField(string label, FrameworkElement input)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(input);
        return panel;
    }

    private static StackPanel DialogButtons(Window window, string okText, string cancelText)
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
}
