using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.App;

public partial class MainWindow
{
    private bool _scheduleEditorReliabilityInitialized;
    private bool _restoringScheduleEditingIdentity;
    private bool _scheduleEditorIsNew;
    private CalendarEvent? _activeScheduleEditingEvent;
    private CalendarEvent? _lastSelectedScheduleEvent;
    private readonly HashSet<Window> _observedScheduleEditorWindows = [];

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_scheduleEditorReliabilityInitialized)
        {
            return;
        }

        _scheduleEditorReliabilityInitialized = true;
        _lastSelectedScheduleEvent = _viewModel.SelectedEvent is { IsTodoLike: false } selected ? selected : null;
        _viewModel.PropertyChanged += PreserveScheduleEditingIdentity;
        Deactivated += ScheduleEditorReliability_Deactivated;
        PreviewMouseLeftButtonDown += ReliableEventSegment_PreviewMouseLeftButtonDown;

        // The selected-day grids are used by both the month side pane and day view.
        // Their legacy path performs a full date navigation before opening an editor.
        foreach (var grid in FindLogicalChildren<DataGrid>(this)
                     .Where(grid => ReferenceEquals(grid.ItemsSource, _viewModel.SelectedDayEvents)))
        {
            grid.MouseDoubleClick -= SelectedDayEventsGrid_MouseDoubleClick;
            grid.MouseDoubleClick += ReliableSelectedDayEventsGrid_MouseDoubleClick;
        }
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
            await OpenSelectedScheduleEditorReliablyAsync();
        }, nameof(ReliableSelectedDayEventsGrid_MouseDoubleClick));
    }

    private async void ReliableEventSegment_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2
            || FindCalendarEventSegment(e.OriginalSource) is not { Event: { IsTodoLike: false } } segment)
        {
            return;
        }

        _dragStartPoint = null;
        _dragSegment = null;
        e.Handled = true;
        await RunUiActionAsync(async () =>
        {
            _viewModel.SelectEventSegment(segment);
            await OpenSelectedScheduleEditorReliablyAsync();
        }, nameof(ReliableEventSegment_PreviewMouseLeftButtonDown));
    }

    private async Task OpenSelectedScheduleEditorReliablyAsync()
    {
        if (_viewModel.SelectedEvent is not { IsTodoLike: false } editingEvent)
        {
            return;
        }

        _scheduleEditorIsNew = false;
        _activeScheduleEditingEvent = editingEvent;
        _lastSelectedScheduleEvent = editingEvent;
        try
        {
            using (_applicationInteractionGuard.EnterOwnedModal())
            {
                await ShowScheduleDialogAsync();
            }
        }
        finally
        {
            _activeScheduleEditingEvent = null;
            _scheduleEditorIsNew = false;
        }
    }

    private void ScheduleEditorReliability_Deactivated(object? sender, EventArgs e)
    {
        QueueScheduleEditorAttachment();
    }

    private void QueueScheduleEditorAttachment()
    {
        TryAttachScheduleEditorWindow();
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(TryAttachScheduleEditorWindow));
    }

    private void TryAttachScheduleEditorWindow()
    {
        var window = FindScheduleEditorWindow();
        if (window is null)
        {
            return;
        }

        _scheduleEditorIsNew = string.Equals(window.Title, "スケジュールの追加", StringComparison.Ordinal);
        if (!_scheduleEditorIsNew
            && _activeScheduleEditingEvent is null
            && string.Equals(window.Title, "スケジュールの編集", StringComparison.Ordinal))
        {
            _activeScheduleEditingEvent = _viewModel.SelectedEvent is { IsTodoLike: false } selected
                ? selected
                : _lastSelectedScheduleEvent;
        }

        if (_observedScheduleEditorWindows.Add(window))
        {
            window.Closed += ScheduleEditorWindow_Closed;
        }

        RestoreScheduleEditingIdentity();
    }

    private void ScheduleEditorWindow_Closed(object? sender, EventArgs e)
    {
        RestoreScheduleEditingIdentity();
        if (sender is Window window)
        {
            window.Closed -= ScheduleEditorWindow_Closed;
            _observedScheduleEditorWindows.Remove(window);
        }
        _activeScheduleEditingEvent = null;
        _scheduleEditorIsNew = false;
    }

    private void PreserveScheduleEditingIdentity(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewModels.MainViewModel.SelectedEvent))
        {
            return;
        }

        var window = FindScheduleEditorWindow();
        if (window is null)
        {
            if (_viewModel.SelectedEvent is { IsTodoLike: false } selected)
            {
                _lastSelectedScheduleEvent = selected;
                // EventList and other owned modal windows can already have MainWindow
                // inactive before a nested schedule editor is opened. In that case
                // MainWindow.Deactivated will not fire again, so attach on the next
                // dispatcher turn after the target schedule is selected.
                QueueScheduleEditorAttachment();
            }
            return;
        }

        _scheduleEditorIsNew = string.Equals(window.Title, "スケジュールの追加", StringComparison.Ordinal);
        if (!_scheduleEditorIsNew
            && _activeScheduleEditingEvent is null
            && string.Equals(window.Title, "スケジュールの編集", StringComparison.Ordinal))
        {
            _activeScheduleEditingEvent = _lastSelectedScheduleEvent;
        }
        RestoreScheduleEditingIdentity();
    }

    private void RestoreScheduleEditingIdentity()
    {
        if (_restoringScheduleEditingIdentity)
        {
            return;
        }

        if (_scheduleEditorIsNew)
        {
            if (_viewModel.SelectedEvent is null)
            {
                return;
            }

            _restoringScheduleEditingIdentity = true;
            try
            {
                _viewModel.SelectedEvent = null;
            }
            finally
            {
                _restoringScheduleEditingIdentity = false;
            }
            return;
        }

        if (_activeScheduleEditingEvent is not { } editingEvent
            || (_viewModel.SelectedEvent is not null
                && string.Equals(_viewModel.SelectedEvent.Id, editingEvent.Id, StringComparison.Ordinal)))
        {
            return;
        }

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

    private void EnsureScheduleSaveIdentity(CalendarEvent? editingEvent)
    {
        if (editingEvent is null)
        {
            if (_viewModel.SelectedEvent is not null)
            {
                _viewModel.SelectedEvent = null;
            }
            return;
        }

        if (_viewModel.SelectedEvent is { } current && SameScheduleOccurrence(current, editingEvent))
        {
            return;
        }

        _viewModel.SelectEvent(editingEvent, selectEventDay: false);
    }

    private static bool SameScheduleOccurrence(CalendarEvent left, CalendarEvent right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal)
        && left.Start == right.Start
        && left.OriginalStart == right.OriginalStart;

    private Window? FindScheduleEditorWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(window =>
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
