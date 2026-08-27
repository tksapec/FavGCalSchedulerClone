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
        }
    }

    private void ScheduleEditorReliability_Deactivated(object? sender, EventArgs e)
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

        if (_activeScheduleEditingEvent is null
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
        EnsureScheduleEditorCalendarSelection(window);
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
            }
            return;
        }

        if (_activeScheduleEditingEvent is null
            && string.Equals(window.Title, "スケジュールの編集", StringComparison.Ordinal))
        {
            _activeScheduleEditingEvent = _lastSelectedScheduleEvent;
        }
        RestoreScheduleEditingIdentity();
    }

    private void RestoreScheduleEditingIdentity()
    {
        if (_restoringScheduleEditingIdentity
            || _activeScheduleEditingEvent is not { } editingEvent
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

    private void EnsureScheduleEditorCalendarSelection(Window scheduleWindow)
    {
        var editingEvent = _activeScheduleEditingEvent;
        var calendarId = editingEvent is not null && !string.IsNullOrWhiteSpace(editingEvent.CalendarId)
            ? editingEvent.CalendarId
            : _viewModel.ResolveScheduleEditorCalendarId(editingEvent);
        if (string.IsNullOrWhiteSpace(calendarId))
        {
            return;
        }

        var calendarSelector = FindVisualDescendants<ComboBox>(scheduleWindow)
            .FirstOrDefault(combo => ReferenceEquals(combo.ItemsSource, _viewModel.AvailableCalendars));
        if (calendarSelector is null)
        {
            return;
        }

        var calendarOptions = _viewModel.CreateScheduleEditorCalendarOptions(editingEvent, calendarId);
        calendarSelector.ItemsSource = calendarOptions;
        calendarSelector.SelectedValue = calendarId;
        if (calendarSelector.SelectedIndex < 0 && calendarOptions.Count > 0)
        {
            calendarSelector.SelectedIndex = 0;
            calendarId = calendarOptions[0].Id;
        }

        _viewModel.EditorCalendarId = calendarId;
    }

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

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
