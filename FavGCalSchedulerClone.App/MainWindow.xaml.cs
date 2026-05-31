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
using FavGCalSchedulerClone.App.Views.Dialogs;
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
    private Point? _dragStartPoint;
    private CalendarEventSegment? _dragSegment;
    private CalendarDay? _dragOverDay;
    private DialogUiFactory DialogUi => new(this, _viewModel.EventColorOptions, _viewModel.SideListFontSize);

    public MainWindow()
    {
        InitializeComponent();
        var repository = new CalendarRepository();
        _viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        _reminderService = new ReminderNotificationService(repository);
        _automaticSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _automaticSyncTimer.Tick += async (_, _) => await _viewModel.RunAutomaticSyncIfDueAsync();
        DataContext = _viewModel;
        _viewModel.SetManualSyncPreviewConfirmation(preview => Task.FromResult(SyncDialogs.ShowPreview(this, preview) == true));
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
        if (_exitRequested)
        {
            _reminderService.Stop();
            _reminderService.Dispose();
            _automaticSyncTimer.Stop();
            StopPreviewSound();
            ToastNotificationManagerCompat.OnActivated -= ToastNotificationManagerCompat_OnActivated;
            return;
        }

        e.Cancel = true;
        Hide();
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
        if (IsEventSegmentSource(e.OriginalSource))
        {
            e.Handled = true;
            return;
        }

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

    private async void EventSegment_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEventSegment { Event: not null } segment })
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            _dragStartPoint = null;
            _dragSegment = null;
            _viewModel.SelectEventSegment(segment);
            e.Handled = true;
            await OpenSelectedEventEditorAsync();
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        _dragSegment = segment;
    }

    private void EventSegment_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not FrameworkElement element
            || _dragStartPoint is not Point startPoint
            || _dragSegment is not { Event: not null } segment
            || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - startPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - startPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStartPoint = null;
        _dragSegment = null;
        _viewModel.SelectEventSegment(segment);
        DragDrop.DoDragDrop(element, segment, DragDropEffects.Move);
        ClearDragTarget();
    }

    private void EventSegment_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEventSegment { Event: not null } segment }
            || e.ClickCount >= 2)
        {
            return;
        }

        _viewModel.SelectEventSegment(segment);
        e.Handled = true;
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

    private void DayCell_DragOver(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CalendarDay day }
            && e.Data.GetDataPresent(typeof(CalendarEventSegment))
            && e.Data.GetData(typeof(CalendarEventSegment)) is CalendarEventSegment segment
            && segment.Event is not null
            && day.Date.Date != segment.Date.Date)
        {
            SetDragTarget(day);
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            ClearDragTarget();
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void DayCell_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CalendarDay day } && ReferenceEquals(_dragOverDay, day))
        {
            ClearDragTarget();
        }
    }

    private async void DayCell_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: CalendarDay targetDay }
            || e.Data.GetData(typeof(CalendarEventSegment)) is not CalendarEventSegment { Event: not null } segment
            || targetDay.Date.Date == segment.Date.Date)
        {
            ClearDragTarget();
            return;
        }

        ClearDragTarget();
        RecurrenceEditScope? recurrenceScope = null;
        if (segment.Event.IsRecurringSeriesItem)
        {
            recurrenceScope = PromptRecurrenceScope(false);
            if (recurrenceScope is null)
            {
                return;
            }
        }

        await _viewModel.MoveEventAsync(segment.Event, segment.Date, targetDay.Date, recurrenceScope);
    }

    private void SetDragTarget(CalendarDay day)
    {
        if (ReferenceEquals(_dragOverDay, day))
        {
            return;
        }

        ClearDragTarget();
        _dragOverDay = day;
        day.IsDropTarget = true;
    }

    private void ClearDragTarget()
    {
        if (_dragOverDay is null)
        {
            return;
        }

        _dragOverDay.IsDropTarget = false;
        _dragOverDay = null;
    }

    private static bool IsEventSegmentSource(object? source)
    {
        if (source is not DependencyObject dependencyObject)
        {
            return false;
        }

        var current = dependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: CalendarEventSegment { Event: not null } })
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
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

    private async void SyncMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _viewModel.SynchronizeManuallyWithPreviewAsync();
            if (result is not null)
            {
                MessageBox.Show(
                    this,
                    $"同期が完了しました。\n\n送信: {result.Pushed} 件\n取得: {result.Pulled} 件\nスキップ: {result.Skipped} 件\n競合: {result.Conflicts} 件\n失敗: {result.Failed} 件",
                    "Google同期",
                    MessageBoxButton.OK,
                    result.Failed == 0 && result.Conflicts == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Google同期エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SyncDiagnosticsMenu_Click(object sender, RoutedEventArgs e)
    {
        await ShowSyncDiagnosticsDialogAsync();
    }

    private async void DeleteEventButton_Click(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedEventWithOptionalConfirmationAsync();
    }

    private async void SelectedDayEventsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var calendarEvent = DataGridDoubleClickHelper.GetEditableRowItem<CalendarEvent>(e.OriginalSource);
        if (calendarEvent is null)
        {
            return;
        }

        await OpenGridEventEditorAsync(calendarEvent);
    }

    private void SidePanelEventsGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || DataGridDoubleClickHelper.IsChromeTarget(e.OriginalSource))
        {
            return;
        }

        var calendarEvent = DataGridDoubleClickHelper.GetEditableRowItem<CalendarEvent>(e.OriginalSource);
        if (calendarEvent is null)
        {
            _viewModel.SelectedEvent = null;
        }
        else
        {
            _viewModel.SelectEvent(calendarEvent, selectEventDay: false);
        }

        e.Handled = true;
        ShowCalendarContextMenu(element);
    }

    private async void TodoEventsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var calendarEvent = DataGridDoubleClickHelper.GetEditableRowItem<CalendarEvent>(e.OriginalSource);
        if (calendarEvent?.IsTodoLike != true)
        {
            return;
        }

        await OpenGridEventEditorAsync(calendarEvent);
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
        if (calendarEvent.IsTodoLike)
        {
            await ShowSelectedTodoDialogAsync();
            return;
        }

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

    private async Task OpenGridEventEditorAsync(CalendarEvent calendarEvent)
    {
        try
        {
            if (calendarEvent.IsTodoLike)
            {
                _viewModel.SelectEvent(calendarEvent, selectEventDay: false);
                await ShowSelectedTodoDialogAsync(calendarEvent);
                return;
            }

            await OpenCalendarEventEditorAsync(calendarEvent);
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"編集画面を開けませんでした: {ex.Message}";
            MessageBox.Show(this, ex.Message, "編集エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        AboutDialog.Show(this);
    }

    internal void ExitFromTray()
    {
        _exitRequested = true;
        Close();
    }

    private async Task ShowScheduleDialogAsync()
    {
        var date = _viewModel.SelectedDay?.Date ?? DateTime.Today;
        var editingEvent = _viewModel.SelectedEvent;
        if (editingEvent is null)
        {
            _viewModel.BeginNewEvent(date);
        }

        var result = ScheduleEditorDialog.Show(
            DialogUi,
            new ScheduleEditorRequest(
                editingEvent is null,
                editingEvent?.Start.Date ?? date,
                editingEvent is null ? date : (editingEvent.IsAllDay ? editingEvent.End.Date.AddDays(-1) : editingEvent.End.Date),
                editingEvent?.Start.ToString("HH:mm") ?? "09:00",
                editingEvent?.End.ToString("HH:mm") ?? "10:00",
                editingEvent?.IsAllDay ?? _viewModel.DefaultNewEventIsAllDay,
                editingEvent?.ReminderMinutesBeforeStart ?? _viewModel.DefaultScheduleReminderMinutes,
                editingEvent?.Location ?? _viewModel.Location,
                editingEvent?.CalendarId ?? _viewModel.EditorCalendarId,
                editingEvent?.ColorId,
                editingEvent?.Title ?? _viewModel.Title,
                editingEvent?.Description ?? string.Empty,
                await _viewModel.LoadScheduleLocationHistoryAsync(),
                await _viewModel.LoadScheduleTitleHistoryAsync(),
                _viewModel.AvailableCalendars,
                _viewModel.ReminderOptions),
            () =>
            {
                if (!_viewModel.HideMainWindowWhileEditingSchedule)
                {
                    return false;
                }

                Hide();
                return true;
            },
            () =>
            {
                Show();
                Activate();
            });
        if (result is null)
        {
            return;
        }

        _viewModel.EditorCalendarId = result.CalendarId;
        _viewModel.EditorColorId = result.ColorId;
        _viewModel.StartDate = result.StartDate;
        _viewModel.EndDate = result.EndDate;
        _viewModel.StartTime = result.StartTime;
        _viewModel.EndTime = result.EndTime;
        _viewModel.IsAllDay = result.IsAllDay;
        _viewModel.ReminderMinutesBeforeStart = result.ReminderMinutesBeforeStart;
        _viewModel.Location = result.Location;
        _viewModel.Title = result.Title;
        _viewModel.Description = result.Description;
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
    private async Task ShowSelectedTodoDialogAsync(CalendarEvent? selectedTodo = null)
    {
        var editingTodo = selectedTodo ?? _viewModel.SelectedEvent;
        if (editingTodo?.IsTodoLike != true)
        {
            return;
        }

        _viewModel.SelectEvent(editingTodo, selectEventDay: false);
        var date = editingTodo.Start.Date;
        var result = TodoEditorDialog.Show(DialogUi, new TodoEditorRequest(
            false,
            date,
            string.IsNullOrWhiteSpace(editingTodo.TodoPriority) ? "A" : editingTodo.TodoPriority,
            editingTodo.TodoProgress,
            editingTodo.CalendarId,
            editingTodo.ColorId,
            editingTodo.Title,
            TagService.GetTodoBodyForEditing(editingTodo.Description),
            _viewModel.AvailableCalendars));
        if (result is null)
        {
            return;
        }

        _viewModel.EditorCalendarId = result.CalendarId;
        _viewModel.EditorColorId = result.ColorId;
        await _viewModel.SaveTodoAsync(
            editingTodo,
            result.DueDate,
            result.Priority,
            result.Progress,
            result.Title,
            result.Description);
    }
    private async Task ShowTodoDialogAsync()
    {
        var date = _viewModel.SelectedDay?.Date ?? DateTime.Today;
        var result = TodoEditorDialog.Show(DialogUi, new TodoEditorRequest(
            true,
            date,
            "A",
            0,
            _viewModel.EditorCalendarId,
            null,
            string.Empty,
            string.Empty,
            _viewModel.AvailableCalendars));
        if (result is null)
        {
            return;
        }

        _viewModel.EditorCalendarId = result.CalendarId;
        _viewModel.EditorColorId = result.ColorId;
        await _viewModel.SaveTodoAsync(
            result.DueDate,
            result.Priority,
            result.Progress,
            result.Title,
            result.Description);
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
        var repairExistingTodoDescriptions = new CheckBox { Content = "既存ToDoの内容を元データで修復する", IsChecked = false };
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
        root.Children.Add(repairExistingTodoDescriptions);
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
                RepairExistingColors: repairExistingColors.IsChecked == true,
                RepairExistingTodoDescriptions: repairExistingTodoDescriptions.IsChecked == true));

            var comparisonText = result.ComparisonSummary is null
                ? ""
                : $"\n\n照合結果\n一致: {result.ComparisonSummary.MatchedCount} 件\n本アプリのみ: {result.ComparisonSummary.LocalOnlyCount} 件\nGoogleエクスポートのみ: {result.ComparisonSummary.ExportOnlyCount} 件";

            MessageBox.Show(
                this,
                $"取り込みが完了しました。\n\n追加: {result.ImportedCount} 件\n既存紐付け: {result.LinkedExistingGoogleCount} 件\n重複スキップ: {result.SkippedDuplicateCount} 件\nラベル色補正: {result.CorrectedColorCount} 件\nToDo内容修復: {result.CorrectedTodoDescriptionCount} 件\n復元不能ToDo: {result.UnrestoredTodoCount} 件\n解析エラー: {result.ParseErrorCount} 件{comparisonText}",
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
            var calendarEvent = DataGridDoubleClickHelper.GetEditableRowItem<CalendarEvent>(e.OriginalSource);
            if (calendarEvent is null)
            {
                return;
            }

            await OpenGridEventEditorAsync(calendarEvent);

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
        grid.Columns.Add(DialogUi.CreateColoredTitleColumn(new DataGridLength(1, DataGridLengthUnitType.Star)));
        panel.Children.Add(grid);

        window.ShowDialog();
    }

    private async Task ShowReminderHistoryDialogAsync()
    {
        var history = await _reminderService.LoadHistoryAsync();
        ReminderHistoryDialog.Show(this, history, OpenReminderHistoryItemAsync);
    }

    private async Task ShowSyncDiagnosticsDialogAsync()
    {
        var diagnostics = await _viewModel.LoadSyncDiagnosticsAsync();
        SyncDialogs.ShowDiagnostics(this, diagnostics, _viewModel.ClearSyncDiagnosticsAsync);
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
        var result = await SettingsDialog.ShowAsync(
            DialogUi,
            new SettingsDialogRequest(
                _viewModel.CreateSettingsSnapshot(),
                _viewModel.OAuthClientJsonPath,
                _viewModel.ReminderOptions,
                _viewModel.ClearScheduleLocationHistoryAsync,
                _viewModel.ClearScheduleTitleHistoryAsync,
                PlayPreviewSound,
                StopPreviewSound,
                _viewModel.SetOAuthClientJsonPathAsync,
                _viewModel.AuthorizeGoogleAsync,
                _viewModel.ClearTokensAsync,
                _viewModel.ReloadAvailableCalendarsAsync));
        if (result is null)
        {
            return;
        }

        await _viewModel.SetOAuthClientJsonPathAsync(result.OAuthClientJsonPath);
        await _viewModel.SaveApplicationSettingsAsync(result.Settings);
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
