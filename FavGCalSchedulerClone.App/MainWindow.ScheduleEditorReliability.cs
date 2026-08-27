using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.App;

public partial class MainWindow
{
    private bool _scheduleEditorReliabilityInitialized;
    private bool _restoringScheduleEditingIdentity;
    private CalendarEvent? _activeScheduleEditingEvent;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_scheduleEditorReliabilityInitialized)
        {
            return;
        }

        _scheduleEditorReliabilityInitialized = true;
        _viewModel.PropertyChanged += PreserveScheduleEditingIdentity;

        // The selected-day grids are used by both the month side pane and day view.
        // Their legacy path navigates/reloads the calendar before opening the editor.
        foreach (var grid in FindLogicalChildren<DataGrid>(this)
                     .Where(grid => ReferenceEquals(grid.ItemsSource, _viewModel.SelectedDayEvents)))
        {
            grid.MouseDoubleClick -= SelectedDayEventsGrid_MouseDoubleClick;
            grid.MouseDoubleClick += ReliableSelectedDayEventsGrid_MouseDoubleClick;
        }

        DayList.MouseDoubleClick -= MonthDayList_MouseDoubleClick;
        DayList.MouseDoubleClick += ReliableMonthDayList_MouseDoubleClick;

        foreach (var list in FindLogicalChildren<ListBox>(this)
                     .Where(list => !ReferenceEquals(list, DayList)
                         && ReferenceEquals(list.ItemsSource, _viewModel.VisibleCalendarDays)))
        {
            list.MouseDoubleClick -= DayList_MouseDoubleClick;
            list.MouseDoubleClick += ReliableWeekDayList_MouseDoubleClick;
        }

        // Week-view event segments use a template-level handler. Intercept only a
        // schedule double click at the window preview stage so ToDo and drag behavior
        // continue through the existing handlers unchanged.
        PreviewMouseLeftButtonDown += ReliableEventSegment_PreviewMouseLeftButtonDown;

        // Keep the existing command routing intact, replacing only schedule creation
        // with the reliability wrapper so new appointments also receive a real calendar id.
        _viewModel.SetWindowCommandHandlers(
            () => RunAsOwnedModalAsync(ShowScheduleDialogReliablyAsync),
            () => RunAsOwnedModalAsync(ShowTodoDialogAsync),
            () => RunAsOwnedModalAsync(BackupAllCalendarsAsync),
            () => RunAsOwnedModalAsync(RestoreAllCalendarsAsync),
            () => RunAsOwnedModalAsync(ShowFavGCalSchedulerImportDialogAsync),
            () => RunAsOwnedModalAsync(ImportCsvAsync),
            () => RunAsOwnedModalAsync(ExportCsvAsync),
            () => RunAsOwnedModalAsync(() => ShowEventListDialogAsync("スケジュール一覧", new EventListFilter(string.Empty, EventKindFilter.All, EventSearchRange.Year, _viewModel.CurrentMonth))),
            () => RunAsOwnedModalAsync(ShowSearchDialogAsync),
            () => RunAsOwnedModalAsync(ShowSyncDiagnosticsDialogAsync),
            () => RunAsOwnedModalAsync(ShowSettingsDialogAsync),
            () => RunAsOwnedModalAsync(ShowReminderHistoryDialogAsync),
            () =>
            {
                RunAsOwnedModal(() => AboutDialog.Show(this));
                return Task.CompletedTask;
            },
            () => RunAsOwnedModalAsync(ShowMonthJumpDialogAsync));
    }

    private async void ReliableSelectedDayEventsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            var calendarEvent = DataGridDoubleClickHelper.GetEditableRowItem<CalendarEvent>(e.OriginalSource);
            if (calendarEvent is null)
            {
                return;
            }

            if (calendarEvent.IsTodoLike)
            {
                await OpenGridEventEditorAsync(calendarEvent);
                return;
            }

            _viewModel.SelectEvent(calendarEvent, selectEventDay: false);
            using (_applicationInteractionGuard.EnterOwnedModal())
            {
                await ShowScheduleDialogReliablyAsync();
            }
        }, nameof(ReliableSelectedDayEventsGrid_MouseDoubleClick));
    }

    private async void ReliableMonthDayList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            if (FindMonthEventLayer(e.OriginalSource) is { } layer
                && layer.HitTestSegment(e.GetPosition(layer)) is { Event: not null })
            {
                e.Handled = true;
                return;
            }

            _viewModel.SelectedEvent = null;
            using (_applicationInteractionGuard.EnterOwnedModal())
            {
                await ShowScheduleDialogReliablyAsync();
            }
        }, nameof(ReliableMonthDayList_MouseDoubleClick));
    }

    private async void ReliableWeekDayList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            if (IsEventSegmentSource(e.OriginalSource))
            {
                e.Handled = true;
                return;
            }

            _viewModel.SelectedEvent = null;
            using (_applicationInteractionGuard.EnterOwnedModal())
            {
                await ShowScheduleDialogReliablyAsync();
            }
        }, nameof(ReliableWeekDayList_MouseDoubleClick));
    }

    private async void ReliableEventSegment_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2
            || FindCalendarEventSegment(e.OriginalSource) is not { Event: { IsTodoLike: false } } segment)
        {
            return;
        }

        e.Handled = true;
        await RunUiActionAsync(async () =>
        {
            _viewModel.SelectEventSegment(segment);
            using (_applicationInteractionGuard.EnterOwnedModal())
            {
                await ShowScheduleDialogReliablyAsync();
            }
        }, nameof(ReliableEventSegment_PreviewMouseLeftButtonDown));
    }

    private async Task ShowScheduleDialogReliablyAsync()
    {
        var editingEvent = _viewModel.SelectedEvent;
        EnsureScheduleEditorCalendar(editingEvent);
        _activeScheduleEditingEvent = editingEvent;
        try
        {
            await ShowScheduleDialogAsync();
        }
        finally
        {
            _activeScheduleEditingEvent = null;
        }
    }

    private void PreserveScheduleEditingIdentity(object? sender, PropertyChangedEventArgs e)
    {
        if (_restoringScheduleEditingIdentity
            || e.PropertyName != nameof(ViewModels.MainViewModel.SelectedEvent)
            || _activeScheduleEditingEvent is not { } editingEvent
            || !IsScheduleEditorWindowOpen()
            || (_viewModel.SelectedEvent is not null
                && string.Equals(_viewModel.SelectedEvent.Id, editingEvent.Id, StringComparison.Ordinal)))
        {
            return;
        }

        // The dialog edits local controls. Restoring the original selection here only
        // protects the event identity; the accepted dialog values are applied afterward.
        if (_viewModel.SelectedEvent is null
            || !string.Equals(_viewModel.SelectedEvent.Id, editingEvent.Id, StringComparison.Ordinal))
        {
            _restoringScheduleEditingIdentity = true;
            try
            {
                _viewModel.SelectEvent(editingEvent, selectEventDay: false);
            }
            finally
            {
                _restoringScheduleEditingIdentity = false;
            }
        }
    }

    private void EnsureScheduleEditorCalendar(CalendarEvent? editingEvent)
    {
        _viewModel.EditorCalendarId = _viewModel.ResolveScheduleEditorCalendarId(editingEvent);
    }

    private bool IsScheduleEditorWindowOpen() =>
        Application.Current.Windows.OfType<Window>().Any(window =>
            ReferenceEquals(window.Owner, this)
            && window.IsVisible
            && (string.Equals(window.Title, "スケジュールの編集", StringComparison.Ordinal)
                || string.Equals(window.Title, "スケジュールの追加", StringComparison.Ordinal)));

    private static CalendarEventSegment? FindCalendarEventSegment(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: CalendarEventSegment segment })
            {
                return segment;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
