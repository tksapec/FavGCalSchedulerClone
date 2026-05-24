using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _automaticSyncTimer;
    private MediaPlayer? _previewSoundPlayer;
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();
        var repository = new CalendarRepository();
        _viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        _reminderService = new ReminderNotificationService(repository);
        _automaticSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _automaticSyncTimer.Tick += async (_, _) => await _viewModel.RunAutomaticSyncIfDueAsync();
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
            _automaticSyncTimer.Start();
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
            _automaticSyncTimer.Stop();
            StopPreviewSound();
            ToastNotificationManagerCompat.OnActivated -= ToastNotificationManagerCompat_OnActivated;
            return;
        }

        e.Cancel = true;
        WindowState = WindowState.Minimized;
    }

    private IReminderNotifier CreateReminderNotifier()
    {
        var fallback = new MessageBoxReminderNotifier(this);
        IReminderNotifier notifier = _viewModel.UseWindowsToastNotifications
            ? new FallbackReminderNotifier(new WindowsToastReminderNotifier(), fallback)
            : fallback;
        return _viewModel.EnableReminderSound
            ? new SoundReminderNotifier(notifier, _viewModel.ReminderSoundFilePath, _viewModel.ReminderSoundVolume)
            : notifier;
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
        _viewModel.SelectedEvent = null;
        await ShowScheduleDialogAsync();
    }

    private void DayCell_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CalendarDay day })
        {
            _viewModel.SelectedDay = day;
            _viewModel.SelectedEvent = null;
        }
    }

    private void DayCell_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarDay day } element)
        {
            return;
        }

        _viewModel.SelectedDay = day;
        _viewModel.SelectedEvent = null;
        e.Handled = true;
        ShowCalendarContextMenu(element);
    }

    private async void EventBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEvent calendarEvent })
        {
            return;
        }

        _viewModel.SelectEvent(calendarEvent);
        e.Handled = true;

        if (e.ClickCount >= 2)
        {
            await OpenSelectedEventEditorAsync();
        }
    }

    private void EventBar_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEvent calendarEvent } element)
        {
            return;
        }

        _viewModel.SelectEvent(calendarEvent);
        e.Handled = true;
        ShowCalendarContextMenu(element);
    }

    private async void EventSegment_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEventSegment { Event: not null } segment })
        {
            return;
        }

        _viewModel.SelectEventSegment(segment);
        e.Handled = true;

        if (e.ClickCount >= 2)
        {
            await OpenSelectedEventEditorAsync();
        }
    }

    private void EventSegment_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEventSegment { Event: not null } segment } element)
        {
            return;
        }

        _viewModel.SelectEventSegment(segment);
        e.Handled = true;
        ShowCalendarContextMenu(element);
    }

    private void ShowCalendarContextMenu(FrameworkElement placementTarget)
    {
        var menu = new ContextMenu { PlacementTarget = placementTarget };

        var addSchedule = new MenuItem { Header = "スケジュールの追加" };
        addSchedule.Click += async (_, _) => await ShowScheduleDialogAsync();
        menu.Items.Add(addSchedule);

        var addTodo = new MenuItem { Header = "ToDoの追加" };
        addTodo.Click += async (_, _) => await ShowTodoDialogAsync();
        menu.Items.Add(addTodo);

        menu.Items.Add(new Separator());

        var edit = new MenuItem { Header = "編集", IsEnabled = _viewModel.SelectedEvent is not null };
        edit.Click += async (_, _) =>
        {
            if (_viewModel.SelectedEvent?.IsTodoLike == true)
            {
                await ShowSelectedTodoDialogAsync();
                return;
            }

            await OpenSelectedEventEditorAsync();
        };
        menu.Items.Add(edit);

        var delete = new MenuItem { Header = "削除", IsEnabled = _viewModel.SelectedEvent is not null };
        delete.Click += async (_, _) => await DeleteSelectedEventWithOptionalConfirmationAsync();
        menu.Items.Add(delete);

        var completeTodo = new MenuItem
        {
            Header = "ToDoを完了にする",
            IsEnabled = _viewModel.SelectedEvent?.IsTodoLike == true && !_viewModel.SelectedEvent.IsTodoDone
        };
        completeTodo.Click += async (_, _) =>
        {
            if (_viewModel.SelectedEvent?.IsTodoLike == true)
            {
                await _viewModel.MarkTodoDoneAsync(_viewModel.SelectedEvent);
            }
        };
        menu.Items.Add(completeTodo);

        menu.Items.Add(new Separator());

        var list = new MenuItem { Header = "スケジュール一覧" };
        list.Click += async (_, _) => await ShowEventListDialogAsync("スケジュール一覧", string.Empty);
        menu.Items.Add(list);

        menu.IsOpen = true;
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
        if (!DataGridDoubleClickHelper.IsEditableRowDoubleClickTarget(e.OriginalSource))
        {
            return;
        }

        await OpenSelectedEventEditorAsync();
    }

    private async void TodoEventsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!DataGridDoubleClickHelper.IsEditableRowDoubleClickTarget(e.OriginalSource))
        {
            return;
        }

        await ShowSelectedTodoDialogAsync();
    }

    private async void TodoDoneButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEvent calendarEvent })
        {
            return;
        }

        e.Handled = true;
        _viewModel.SelectEvent(calendarEvent);
        await _viewModel.MarkTodoDoneAsync(calendarEvent);
    }

    private async Task OpenSelectedEventEditorAsync()
    {
        if (_viewModel.SelectedEvent is null)
        {
            return;
        }

        var calendarEvent = _viewModel.SelectedEvent;
        await _viewModel.NavigateToDateAsync(calendarEvent.Start.Date);
        _viewModel.SelectEvent(calendarEvent);
        await ShowScheduleDialogAsync();
    }

    private async Task OpenCalendarEventEditorAsync(CalendarEvent calendarEvent)
    {
        await _viewModel.NavigateToDateAsync(calendarEvent.Start.Date);
        _viewModel.SelectEvent(calendarEvent);
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
        if (editingEvent is null)
        {
            _viewModel.BeginNewEvent(date);
        }

        var window = CreateOwnedDialog(editingEvent is null ? "スケジュールの追加" : "スケジュールの編集", 1120, 720);
        var root = CreateEditorDialogRoot();
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
        var dayCount = new TextBox { Width = 48, Text = "1", HorizontalContentAlignment = HorizontalAlignment.Right };
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
            SelectedValue = editingEvent?.ReminderMinutesBeforeStart ?? _viewModel.DefaultScheduleReminderMinutes
        };
        var location = new ComboBox
        {
            IsEditable = true,
            ItemsSource = await _viewModel.LoadScheduleLocationHistoryAsync(),
            Text = editingEvent?.Location ?? _viewModel.Location
        };
        var calendar = new ComboBox
        {
            ItemsSource = _viewModel.AvailableCalendars,
            DisplayMemberPath = nameof(GoogleCalendarSelectionItem.Summary),
            SelectedValuePath = nameof(GoogleCalendarSelectionItem.Id),
            SelectedValue = editingEvent?.CalendarId ?? _viewModel.EditorCalendarId
        };
        var color = CreateColorComboBox(editingEvent?.ColorId);
        var title = new ComboBox
        {
            IsEditable = true,
            ItemsSource = await _viewModel.LoadScheduleTitleHistoryAsync(),
            Text = editingEvent?.Title ?? _viewModel.Title
        };
        var description = new TextBox
        {
            Text = editingEvent?.Description ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var updatingDateRange = false;
        void UpdateDayCount()
        {
            if (updatingDateRange || startDate.SelectedDate is null || endDate.SelectedDate is null)
            {
                return;
            }

            updatingDateRange = true;
            var days = Math.Max(1, (endDate.SelectedDate.Value.Date - startDate.SelectedDate.Value.Date).Days + 1);
            if (endDate.SelectedDate.Value.Date < startDate.SelectedDate.Value.Date)
            {
                endDate.SelectedDate = startDate.SelectedDate;
            }
            dayCount.Text = days.ToString();
            updatingDateRange = false;
        }
        void UpdateEndDateFromCount()
        {
            if (updatingDateRange || startDate.SelectedDate is null || !int.TryParse(dayCount.Text, out var days))
            {
                return;
            }

            updatingDateRange = true;
            days = Math.Max(1, days);
            dayCount.Text = days.ToString();
            endDate.SelectedDate = startDate.SelectedDate.Value.Date.AddDays(days - 1);
            updatingDateRange = false;
        }
        startDate.SelectedDateChanged += (_, _) => UpdateDayCount();
        endDate.SelectedDateChanged += (_, _) => UpdateDayCount();
        dayCount.LostFocus += (_, _) => UpdateEndDateFromCount();
        UpdateDayCount();

        var timeGroup = new GroupBox { Header = "開始時間／終了時間", Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 14, 14, 6) };
        var timeGrid = new Grid();
        AddEditorColumns(timeGrid, 4);
        AddEditorRow(timeGrid, 0, "開始日", startDate, "終了日", endDate);
        var dayPanel = new StackPanel { Orientation = Orientation.Horizontal };
        dayPanel.Children.Add(dayCount);
        dayPanel.Children.Add(new TextBlock { Text = " 日数", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        AddEditorRow(timeGrid, 1, "", new TextBlock(), "", dayPanel);
        AddEditorRow(timeGrid, 2, "開始時間", startTime, "終了時間", endTime);
        AddEditorRow(timeGrid, 3, "", new TextBlock { Text = "～", HorizontalAlignment = HorizontalAlignment.Right }, "", isAllDay);
        timeGroup.Content = timeGrid;

        var alarmGroup = new GroupBox { Header = "アラーム", Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(14, 14, 14, 6) };
        var alarmGrid = new Grid();
        AddEditorColumns(alarmGrid, 2);
        AddEditorRow(alarmGrid, 0, "通知時間", reminder);
        alarmGroup.Content = alarmGrid;

        var upper = new Grid();
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(timeGroup, 0);
        Grid.SetColumn(alarmGroup, 1);
        upper.Children.Add(timeGroup);
        upper.Children.Add(alarmGroup);
        Grid.SetRow(upper, 0);
        root.Children.Add(upper);

        var detailsGroup = new GroupBox { Header = "予定詳細", Padding = new Thickness(18, 14, 18, 10), Margin = new Thickness(0, 0, 0, 10) };
        var details = new Grid();
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        AddPositionedField(details, 0, 0, "場所", location, 1, 1);
        AddPositionedField(details, 0, 2, "予定の色", color, 3, 1);
        AddPositionedField(details, 0, 4, "カレンダー", calendar, 5, 1);
        AddPositionedField(details, 1, 0, "件名", title, 1, 5);
        AddPositionedField(details, 2, 0, "内容", description, 1, 5);
        Grid.SetRowSpan(description, 2);
        detailsGroup.Content = details;
        Grid.SetRow(detailsGroup, 1);
        root.Children.Add(detailsGroup);

        var buttons = DialogButtons(window, "設定", "キャンセル");
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        var accepted = false;
        var hideMainWindow = _viewModel.HideMainWindowWhileEditingSchedule;
        if (hideMainWindow)
        {
            Hide();
        }

        try
        {
            accepted = window.ShowDialog() == true;
        }
        finally
        {
            if (hideMainWindow)
            {
                Show();
                Activate();
            }
        }

        if (!accepted)
        {
            return;
        }

        UpdateEndDateFromCount();
        _viewModel.EditorCalendarId = calendar.SelectedValue?.ToString() ?? _viewModel.EditorCalendarId;
        _viewModel.EditorColorId = color.SelectedValue?.ToString();
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

    private async Task ShowSelectedTodoDialogAsync()
    {
        if (_viewModel.SelectedEvent?.IsTodoLike != true)
        {
            return;
        }

        var editingTodo = _viewModel.SelectedEvent;
        var date = editingTodo.Start.Date;
        var window = CreateOwnedDialog("ＴＯＤＯの編集", 820, 600);
        var root = CreateEditorDialogRoot();
        window.Content = root;

        var dueDate = new DatePicker { SelectedDate = date };
        var priority = new ComboBox { SelectedIndex = 0, ItemsSource = new[] { "A", "B", "C", "D", "E", "F" } };
        priority.SelectedItem = string.IsNullOrWhiteSpace(editingTodo.TodoPriority) ? "A" : editingTodo.TodoPriority;
        var progress = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 210, Value = editingTodo.TodoProgress };
        var progressLabel = new TextBlock { Text = $"進捗 {editingTodo.TodoProgress}%", VerticalAlignment = VerticalAlignment.Center };
        progress.ValueChanged += (_, _) => progressLabel.Text = $"進捗 {(int)progress.Value}%";
        var calendar = new ComboBox
        {
            ItemsSource = _viewModel.AvailableCalendars,
            DisplayMemberPath = nameof(GoogleCalendarSelectionItem.Summary),
            SelectedValuePath = nameof(GoogleCalendarSelectionItem.Id),
            SelectedValue = editingTodo.CalendarId
        };
        var color = CreateColorComboBox(editingTodo.ColorId);
        var title = new TextBox { Text = editingTodo.Title };
        var description = new TextBox { Text = editingTodo.Description ?? string.Empty, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        AddTodoEditorLayout(root, window, dueDate, priority, progress, progressLabel, color, calendar, title, description);

        if (window.ShowDialog() != true)
        {
            return;
        }

        _viewModel.EditorCalendarId = calendar.SelectedValue?.ToString() ?? _viewModel.EditorCalendarId;
        _viewModel.EditorColorId = color.SelectedValue?.ToString();
        await _viewModel.SaveTodoAsync(
            editingTodo,
            dueDate.SelectedDate ?? date,
            priority.SelectedItem?.ToString() ?? "A",
            (int)progress.Value,
            title.Text,
            description.Text);
    }

    private async Task ShowTodoDialogAsync()
    {
        var date = _viewModel.SelectedDay?.Date ?? DateTime.Today;
        var window = CreateOwnedDialog("ＴＯＤＯの追加", 820, 600);
        var root = CreateEditorDialogRoot();
        window.Content = root;

        var dueDate = new DatePicker { SelectedDate = date };
        var priority = new ComboBox { SelectedIndex = 0, ItemsSource = new[] { "A", "B", "C", "D", "E", "F" } };
        var done = new CheckBox { Content = "進捗(0%)", VerticalAlignment = VerticalAlignment.Center };
        done.Checked += (_, _) => done.Content = "進捗(100%)";
        done.Unchecked += (_, _) => done.Content = "進捗(0%)";
        var calendar = new ComboBox
        {
            ItemsSource = _viewModel.AvailableCalendars,
            DisplayMemberPath = nameof(GoogleCalendarSelectionItem.Summary),
            SelectedValuePath = nameof(GoogleCalendarSelectionItem.Id),
            SelectedValue = _viewModel.EditorCalendarId
        };
        var color = CreateColorComboBox(null);
        var title = new TextBox();
        var description = new TextBox { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        AddTodoEditorLayout(root, window, dueDate, priority, done, new TextBlock(), color, calendar, title, description);

        if (window.ShowDialog() != true)
        {
            return;
        }

        _viewModel.EditorCalendarId = calendar.SelectedValue?.ToString() ?? _viewModel.EditorCalendarId;
        _viewModel.EditorColorId = color.SelectedValue?.ToString();
        await _viewModel.SaveTodoAsync(
            dueDate.SelectedDate ?? date,
            priority.SelectedItem?.ToString() ?? "A",
            done.IsChecked == true ? 100 : 0,
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
        var repairExistingColors = new CheckBox { Content = "既存予定のラベル色を元データで修復する", IsChecked = false };
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

        void ShowImportError(string operation, Exception ex)
        {
            var message = $"{operation}に失敗しました。\n\n{ex.Message}";
            analysisText.Text = message;
            MessageBox.Show(this, message, "FavGCalScheduler データ移行エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }

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
                try
                {
                    oauthPath.Text = dialog.FileName;
                    await _viewModel.SetOAuthClientJsonPathAsync(dialog.FileName);
                }
                catch (Exception ex)
                {
                    ShowImportError("OAuth JSON の設定", ex);
                }
            }
        };
        authorize.Click += async (_, _) =>
        {
            try
            {
                await _viewModel.SetOAuthClientJsonPathAsync(oauthPath.Text);
                await _viewModel.AuthorizeGoogleAsync();
                targetCalendar.ItemsSource = _viewModel.AvailableCalendars;
            }
            catch (Exception ex)
            {
                ShowImportError("Google 認証", ex);
                return;
            }
            analysisText.Text = "Google認証とカレンダー一覧取得が完了しました。";
        };

        var analyze = new Button { Content = "解析", MinWidth = 96 };
        analyze.Click += async (_, _) =>
        {
            try
            {
                analysis = await _viewModel.AnalyzeFavGCalSchedulerImportAsync(sourceFolder.Text);
                analysisText.Text = FormatFavGCalAnalysis(analysis);
            }
            catch (Exception ex)
            {
                analysis = null;
                ShowImportError("解析", ex);
            }
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
        root.Children.Add(repairExistingColors);
        root.Children.Add(verifyGoogle);
        root.Children.Add(WideField("解析結果", analysisText));
        root.Children.Add(DialogButtons(window, "取り込み", "キャンセル"));

        if (window.ShowDialog() != true)
        {
            return;
        }

        try
        {
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
                ComparisonZipPath: string.IsNullOrWhiteSpace(comparisonZip.Text) ? null : comparisonZip.Text,
                RepairExistingColors: repairExistingColors.IsChecked == true));

            var comparisonText = result.ComparisonSummary is null
                ? ""
                : $"\n\n照合結果\n一致: {result.ComparisonSummary.MatchedCount} 件\n本アプリのみ: {result.ComparisonSummary.LocalOnlyCount} 件\nGoogleエクスポートのみ: {result.ComparisonSummary.ExportOnlyCount} 件";

            MessageBox.Show(
                this,
                $"取り込みが完了しました。\n\n追加: {result.ImportedCount} 件\n既存紐付け: {result.LinkedExistingGoogleCount} 件\n重複スキップ: {result.SkippedDuplicateCount} 件\nラベル色補正: {result.CorrectedColorCount} 件\n復元不能ToDo: {result.UnrestoredTodoCount} 件\n解析エラー: {result.ParseErrorCount} 件{comparisonText}",
                "FavGCalSchedulerデータ移行",
                MessageBoxButton.OK,
                result.ParseErrorCount == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ShowImportError("取り込み", ex);
        }
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
        var eventItems = new ObservableCollection<CalendarEvent>(events);

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
            ItemsSource = eventItems,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 24
        };
        grid.MouseDoubleClick += async (_, e) =>
        {
            if (!DataGridDoubleClickHelper.IsEditableRowDoubleClickTarget(e.OriginalSource))
            {
                return;
            }

            if (grid.SelectedItem is not CalendarEvent calendarEvent)
            {
                return;
            }

            if (calendarEvent.IsTodoLike)
            {
                await _viewModel.NavigateToDateAsync(calendarEvent.Start.Date);
                _viewModel.SelectEvent(calendarEvent);
                await ShowSelectedTodoDialogAsync();
            }
            else
            {
                await OpenCalendarEventEditorAsync(calendarEvent);
            }

            eventItems.Clear();
            var refreshedEvents = string.IsNullOrWhiteSpace(query)
                ? await _viewModel.LoadYearEventsAsync(_viewModel.CurrentMonth)
                : await _viewModel.SearchYearEventsAsync(_viewModel.CurrentMonth, query);
            foreach (var refreshedEvent in refreshedEvents)
            {
                eventItems.Add(refreshedEvent);
            }
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "日時", Binding = new Binding(nameof(CalendarEvent.DateDisplayText)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "カレンダー", Binding = new Binding(nameof(CalendarEvent.CalendarId)), Width = 150 });
        grid.Columns.Add(CreateColoredTitleColumn(new DataGridLength(1, DataGridLengthUnitType.Star)));
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
        var settings = _viewModel.CreateSettingsSnapshot();
        var window = CreateOwnedDialog("アプリ設定", 690, 610);
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
        var closeExits = new CheckBox { Content = "ウィンドウの閉じるボタンでアプリを終了する", IsChecked = settings.CloseButtonExitsApplication, Margin = new Thickness(0, 6, 0, 6) };
        var hideEditor = new CheckBox { Content = "スケジュール編集時にメインウィンドウを非表示にする", IsChecked = settings.HideMainWindowWhileEditingSchedule, Margin = new Thickness(0, 6, 0, 6) };
        var noReuse = new CheckBox { Content = "新規入力時、場所や件名に前回の入力内容を設定しない", IsChecked = !settings.ReuseLastScheduleInput, Margin = new Thickness(0, 6, 0, 6) };
        var defaultAllDay = new CheckBox { Content = "スケジュール作成時に終日にチェックを付ける", IsChecked = settings.DefaultNewEventIsAllDay, Margin = new Thickness(0, 6, 0, 6) };
        var defaultReminder = new ComboBox { ItemsSource = _viewModel.ReminderOptions, DisplayMemberPath = nameof(ReminderOption.Label), SelectedValuePath = nameof(ReminderOption.MinutesBeforeStart), SelectedValue = settings.DefaultScheduleReminderMinutes, MinWidth = 190, HorizontalAlignment = HorizontalAlignment.Left };
        appPage.Children.Add(new TextBlock { Text = "起動時のカレンダー表示タイプ" });
        appPage.Children.Add(startupView);
        appPage.Children.Add(new TextBlock { Text = "起動時のToDoタブ表示タイプ" });
        appPage.Children.Add(startupTodo);
        appPage.Children.Add(confirmDelete);
        appPage.Children.Add(closeExits);
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
                await _viewModel.ClearScheduleLocationHistoryAsync();
            }
        };
        clearTitle.Click += async (_, _) =>
        {
            if (MessageBox.Show(window, "件名入力履歴を削除しますか。", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await _viewModel.ClearScheduleTitleHistoryAsync();
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
        testSound.Click += (_, _) => PlayPreviewSound(soundPath.Text, (int)volume.Value);
        stopSound.Click += (_, _) => StopPreviewSound();
        var toast = new CheckBox { Content = "Windowsトースト通知を使う", IsChecked = settings.UseWindowsToastNotifications, Margin = new Thickness(0, 18, 0, 0) };
        notifyPage.Children.Add(soundEnabled);
        notifyPage.Children.Add(soundPath);
        notifyPage.Children.Add(browseSound);
        notifyPage.Children.Add(new TextBlock { Text = "再生音量", Margin = new Thickness(0, 12, 0, 4) });
        notifyPage.Children.Add(volume);
        var soundButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        soundButtons.Children.Add(testSound);
        soundButtons.Children.Add(stopSound);
        notifyPage.Children.Add(soundButtons);
        notifyPage.Children.Add(toast);
        tabs.Items.Add(Tab("通知設定", notifyPage));

        var accountPage = Page();
        var oauthPath = new TextBox { Text = _viewModel.OAuthClientJsonPath, MinWidth = 500, Margin = new Thickness(0, 8, 0, 12) };
        var chooseOAuth = new Button { Content = "OAuth JSONを選択", Width = 140 };
        var authorize = new Button { Content = "Google認証", Width = 120 };
        var clearToken = new Button { Content = "トークン削除", Width = 120 };
        var reloadCalendars = new Button { Content = "カレンダー一覧を更新", Width = 170 };
        chooseOAuth.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Google OAuth client JSON (*.json)|*.json|All files (*.*)|*.*" };
            if (dialog.ShowDialog(window) == true) oauthPath.Text = dialog.FileName;
        };
        authorize.Click += async (_, _) => { await _viewModel.SetOAuthClientJsonPathAsync(oauthPath.Text); await _viewModel.AuthorizeGoogleAsync(); };
        clearToken.Click += (_, _) => _viewModel.ClearTokensCommand.Execute(null);
        reloadCalendars.Click += (_, _) => _viewModel.ReloadCalendarListCommand.Execute(null);
        accountPage.Children.Add(new TextBlock { Text = "Google Calendar API OAuth client JSON" });
        accountPage.Children.Add(oauthPath);
        foreach (var button in new[] { chooseOAuth, authorize, clearToken, reloadCalendars }) accountPage.Children.Add(button);
        tabs.Items.Add(Tab("GoogleAccount設定", accountPage));

        var syncPage = Page();
        var syncAfterChange = new CheckBox { Content = "スケジュールの追加／編集／削除時にGoogleカレンダーと同期を行う", IsChecked = settings.SyncAfterLocalChange, Margin = new Thickness(0, 8, 0, 18) };
        var syncInterval = Options(new object[] { "自動同期しない", "30分", "1時間", "2時間", "6時間" }, settings.AutomaticSyncIntervalMinutes switch { 30 => "30分", 60 => "1時間", 120 => "2時間", 360 => "6時間", _ => "自動同期しない" });
        syncPage.Children.Add(syncAfterChange);
        syncPage.Children.Add(new TextBlock { Text = "スケジュール表示中の自動同期間隔" });
        syncPage.Children.Add(syncInterval);
        tabs.Items.Add(Tab("Googleカレンダー設定", syncPage));

        var buttons = DialogButtons(window, "OK", "キャンセル");
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Remove(tabs);
        root.Children.Add(tabs);
        if (window.ShowDialog() != true)
        {
            StopPreviewSound();
            return;
        }

        StopPreviewSound();
        settings.StartupCalendarViewMode = startupView.SelectedItem is CalendarViewMode mode ? mode : CalendarViewMode.Month;
        settings.StartupTodoTabIndex = startupTodo.SelectedIndex;
        settings.ConfirmBeforeDelete = confirmDelete.IsChecked == true;
        settings.CloseButtonExitsApplication = closeExits.IsChecked == true;
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
        settings.AutomaticSyncIntervalMinutes = syncInterval.SelectedIndex switch { 1 => 30, 2 => 60, 3 => 120, 4 => 360, _ => null };
        await _viewModel.SetOAuthClientJsonPathAsync(oauthPath.Text);
        await _viewModel.SaveApplicationSettingsAsync(settings);
        _reminderService.SetNotifier(CreateReminderNotifier());
    }

    private void PlayPreviewSound(string path, int volume)
    {
        StopPreviewSound();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            _previewSoundPlayer = new MediaPlayer { Volume = Math.Clamp(volume, 0, 100) / 100.0 };
            _previewSoundPlayer.Open(new Uri(path, UriKind.Absolute));
            _previewSoundPlayer.Play();
        }
        catch
        {
            StopPreviewSound();
        }
    }

    private void StopPreviewSound()
    {
        _previewSoundPlayer?.Stop();
        _previewSoundPlayer?.Close();
        _previewSoundPlayer = null;
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
            $"復元不能ToDo: {analysis.UnrestoredTodoCount} 件",
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

    private static Grid CreateEditorDialogRoot()
    {
        var grid = new Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    private ComboBox CreateColorComboBox(string? selectedColorId)
    {
        var combo = new ComboBox
        {
            ItemsSource = _viewModel.EventColorOptions,
            SelectedValuePath = nameof(EventColorSelectionItem.Id),
            SelectedValue = selectedColorId,
            MinWidth = 106
        };

        var template = new DataTemplate(typeof(EventColorSelectionItem));
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var color = new FrameworkElementFactory(typeof(Border));
        color.SetValue(Border.WidthProperty, 54.0);
        color.SetValue(Border.HeightProperty, 14.0);
        color.SetValue(Border.BorderBrushProperty, System.Windows.Media.Brushes.SlateGray);
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

    private DataGridTemplateColumn CreateColoredTitleColumn(DataGridLength width)
    {
        var template = new DataTemplate(typeof(CalendarEvent));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BorderBrushProperty, System.Windows.Media.Brushes.SlateGray);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new Thickness(4, 1, 4, 1));
        border.SetValue(Border.MarginProperty, new Thickness(1));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(CalendarEvent.DisplayColor)));
        border.SetBinding(Border.ToolTipProperty, new Binding(nameof(CalendarEvent.ToolTipText)));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(CalendarEvent.Title)));
        text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(CalendarEvent.DisplayForegroundColor)));
        text.SetValue(TextBlock.FontSizeProperty, _viewModel.SideListFontSize);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        border.AppendChild(text);
        template.VisualTree = border;
        return new DataGridTemplateColumn { Header = "件名", CellTemplate = template, Width = width };
    }

    private static void AddEditorColumns(Grid grid, int columns)
    {
        for (var index = 0; index < columns; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = index % 2 == 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star)
            });
        }
    }

    private static void AddEditorRow(Grid grid, int row, string leftLabel, FrameworkElement leftInput)
    {
        AddEditorRow(grid, row, leftLabel, leftInput, "", new TextBlock());
    }

    private static void AddEditorRow(
        Grid grid,
        int row,
        string leftLabel,
        FrameworkElement leftInput,
        string rightLabel,
        FrameworkElement rightInput)
    {
        while (grid.RowDefinitions.Count <= row)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        AddPositionedField(grid, row, 0, leftLabel, leftInput, 1, 1);
        if (grid.ColumnDefinitions.Count >= 4)
        {
            AddPositionedField(grid, row, 2, rightLabel, rightInput, 3, 1);
        }
    }

    private static void AddPositionedField(
        Grid grid,
        int row,
        int labelColumn,
        string label,
        FrameworkElement input,
        int inputColumn,
        int inputColumnSpan)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            var text = new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 10, 12),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, labelColumn);
            grid.Children.Add(text);
        }

        input.Margin = new Thickness(0, 0, 16, 12);
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(input, row);
        Grid.SetColumn(input, inputColumn);
        Grid.SetColumnSpan(input, inputColumnSpan);
        grid.Children.Add(input);
    }

    private static void AddTodoEditorLayout(
        Grid root,
        Window window,
        DatePicker dueDate,
        ComboBox priority,
        FrameworkElement progressInput,
        FrameworkElement progressValue,
        ComboBox color,
        ComboBox calendar,
        TextBox title,
        TextBox description)
    {
        var dueGroup = new GroupBox { Header = "期限／進捗", Padding = new Thickness(16, 16, 16, 8), Margin = new Thickness(0, 0, 10, 10) };
        var dueGrid = new Grid();
        AddEditorColumns(dueGrid, 2);
        AddEditorRow(dueGrid, 0, "期限", dueDate);
        var progressPanel = new StackPanel { Orientation = Orientation.Horizontal };
        progressPanel.Children.Add(progressInput);
        if (progressValue is TextBlock text && !string.IsNullOrWhiteSpace(text.Text))
        {
            progressPanel.Children.Add(progressValue);
        }
        AddEditorRow(dueGrid, 1, "", progressPanel);
        dueGroup.Content = dueGrid;

        var priorityGroup = new GroupBox { Header = "優先度", Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 10) };
        var priorityGrid = new Grid();
        AddEditorColumns(priorityGrid, 2);
        AddEditorRow(priorityGrid, 0, "優先度", priority);
        priorityGroup.Content = priorityGrid;

        var upper = new Grid();
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(dueGroup, 0);
        Grid.SetColumn(priorityGroup, 1);
        upper.Children.Add(dueGroup);
        upper.Children.Add(priorityGroup);
        Grid.SetRow(upper, 0);
        root.Children.Add(upper);

        var detailsGroup = new GroupBox { Header = "ToDo詳細", Padding = new Thickness(18, 14, 18, 10), Margin = new Thickness(0, 0, 0, 10) };
        var details = new Grid();
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddPositionedField(details, 0, 0, "予定の色", color, 1, 1);
        AddPositionedField(details, 0, 2, "カレンダー", calendar, 3, 1);
        AddPositionedField(details, 1, 0, "件名", title, 1, 3);
        AddPositionedField(details, 2, 0, "内容", description, 1, 3);
        detailsGroup.Content = details;
        Grid.SetRow(detailsGroup, 1);
        root.Children.Add(detailsGroup);

        var buttons = DialogButtons(window, "設定", "キャンセル");
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
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
