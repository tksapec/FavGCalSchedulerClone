using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FavGCalSchedulerClone.App.Commands;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Win32;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    private const int TodoListDisplayLimit = 100;
    private bool _showAllIncompleteTodos;
    private bool _showAllCompletedTodos;
    private int _incompleteTodosTotalCount;
    private int _completedTodosTotalCount;
    private AsyncRelayCommand? _toggleIncompleteTodoListLimitCommand;
    private AsyncRelayCommand? _toggleCompletedTodoListLimitCommand;

    public AsyncRelayCommand ToggleIncompleteTodoListLimitCommand =>
        _toggleIncompleteTodoListLimitCommand ??= CreateAsyncCommand(ToggleIncompleteTodoListLimitAsync);

    public AsyncRelayCommand ToggleCompletedTodoListLimitCommand =>
        _toggleCompletedTodoListLimitCommand ??= CreateAsyncCommand(ToggleCompletedTodoListLimitAsync);

    public int TodoEventsRemainingCount => Math.Max(0, _incompleteTodosTotalCount - TodoEvents.Count);
    public int CompletedTodoEventsRemainingCount => Math.Max(0, _completedTodosTotalCount - CompletedTodoEvents.Count);
    public string TodoEventsCountText => $"表示中 {TodoEvents.Count} / {_incompleteTodosTotalCount} 件";
    public string CompletedTodoEventsCountText => $"表示中 {CompletedTodoEvents.Count} / {_completedTodosTotalCount} 件";
    public string TodoEventsLimitButtonText => _showAllIncompleteTodos ? "100件に戻す" : $"残り {TodoEventsRemainingCount} 件を表示";
    public string CompletedTodoEventsLimitButtonText => _showAllCompletedTodos ? "100件に戻す" : $"残り {CompletedTodoEventsRemainingCount} 件を表示";
    public bool IsTodoEventsLimitControlVisible => TodoEventsRemainingCount > 0 || _showAllIncompleteTodos;
    public bool IsCompletedTodoEventsLimitControlVisible => CompletedTodoEventsRemainingCount > 0 || _showAllCompletedTodos;

    public async Task ToggleIncompleteTodoListLimitAsync()
    {
        _showAllIncompleteTodos = !_showAllIncompleteTodos;
        await RefreshTodosAsync();
    }

    public async Task ToggleCompletedTodoListLimitAsync()
    {
        _showAllCompletedTodos = !_showAllCompletedTodos;
        await RefreshTodosAsync();
    }

    public async Task SaveTodoAsync(DateTime dueDate, string priority, int progress, string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            Status = "件名を入力してください。";
            return;
        }

        var todoEvent = new CalendarEvent
        {
            Title = title.Trim(),
            Description = TagService.UpdateTodoMarker(description, priority, progress),
            CalendarId = ResolveEditorCalendarId(),
            Start = new DateTimeOffset(dueDate.Date),
            End = new DateTimeOffset(dueDate.Date.AddDays(1)),
            IsAllDay = true,
            IsDirty = true,
            IsDeleted = false,
            IsTodoLike = true,
            ReminderMinutesBeforeStart = null,
            AppReminderMinutesBeforeStart = [],
            GoogleEmailReminderMinutesBeforeStart = [],
            IsAppReminderEnabled = false,
            IsGoogleEmailReminderEnabled = false,
            GoogleReminderMetadata = null,
            ColorId = EditorColorId
        };

        await _repository.SaveEventAsync(todoEvent);
        CaptureUndo("ToDo追加", [], [todoEvent.Id]);
        await RefreshCalendarAsync();
        Status = "ToDoを保存しました。同期するとGoogleカレンダーへ反映されます。";
        await SyncAfterLocalChangeAsync();
    }

    public async Task SaveTodoAsync(CalendarEvent editingTodo, DateTime dueDate, string priority, int progress, string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            Status = "件名を入力してください。";
            return;
        }

        var originalTodo = CloneEventForEditing(editingTodo);
        var updatedTodo = CloneEventForEditing(editingTodo);
        updatedTodo.Title = title.Trim();
        updatedTodo.Description = TagService.UpdateTodoMarker(description, priority, progress);
        updatedTodo.CalendarId = ResolveEditorCalendarId();
        updatedTodo.Start = new DateTimeOffset(dueDate.Date);
        updatedTodo.End = new DateTimeOffset(dueDate.Date.AddDays(1));
        updatedTodo.IsAllDay = true;
        updatedTodo.IsDeleted = false;
        updatedTodo.IsTodoLike = true;
        TodoReminderPolicy.NormalizeLocalFields(updatedTodo);
        updatedTodo.ColorId = EditorColorId;

        if (!EventDirtyFieldTracker.HasChanges(originalTodo, updatedTodo))
        {
            Status = "ToDoに変更はありません。";
            return;
        }

        updatedTodo.IsDirty = true;
        await SaveEventWithCalendarMoveAsync(updatedTodo, originalTodo);
        CaptureUndo("ToDo編集", [originalTodo]);
        await RefreshCalendarAsync();
        SelectedEvent = _visibleEvents.FirstOrDefault(item => item.Id == updatedTodo.Id) ?? updatedTodo;
        Status = "ToDoを保存しました。同期するとGoogleカレンダーへ反映されます。";
        await SyncAfterLocalChangeAsync();
    }

    public async Task SaveTodoAsync(string eventId, DateTime dueDate, string priority, int progress, string title, string? description)
    {
        var editingTodo = _storedEvents.FirstOrDefault(item => item.Id == eventId)
            ?? _visibleEvents.FirstOrDefault(item => item.Id == eventId);
        if (editingTodo is null)
        {
            await SaveTodoAsync(dueDate, priority, progress, title, description);
            return;
        }

        await SaveTodoAsync(editingTodo, dueDate, priority, progress, title, description);
    }

    public async Task MarkSelectedTodoDoneAsync()
    {
        if (SelectedEvent is null || !SelectedEvent.IsTodoLike)
        {
            return;
        }

        var originalTodo = CloneEventForEditing(SelectedEvent);
        var updatedTodo = CloneEventForEditing(SelectedEvent);
        var priority = updatedTodo.TodoPriority;
        updatedTodo.Description = TagService.UpdateTodoMarker(updatedTodo.Description, priority, 100);
        updatedTodo.IsDirty = true;
        await _repository.SaveEventAsync(updatedTodo);
        CaptureUndo("ToDo完了", [originalTodo]);
        await RefreshCalendarAsync();
        SelectedEvent = _visibleEvents.FirstOrDefault(item => item.Id == updatedTodo.Id) ?? updatedTodo;
        MarkSelectedTodoDoneCommand.RaiseCanExecuteChanged();
        Status = "ToDoを処理済みにしました。同期するとGoogleカレンダーへ反映されます。";
        await SyncAfterLocalChangeAsync();
    }

    public async Task MarkTodoDoneAsync(CalendarEvent todoEvent)
    {
        if (!todoEvent.IsTodoLike)
        {
            return;
        }

        var originalTodo = CloneEventForEditing(todoEvent);
        var updatedTodo = CloneEventForEditing(todoEvent);
        var priority = updatedTodo.TodoPriority;
        updatedTodo.Description = TagService.UpdateTodoMarker(updatedTodo.Description, priority, 100);
        updatedTodo.IsDirty = true;
        await _repository.SaveEventAsync(updatedTodo);
        CaptureUndo("ToDo完了", [originalTodo]);
        await RefreshCalendarAsync();
        MarkSelectedTodoDoneCommand.RaiseCanExecuteChanged();
        Status = "ToDoを処理済みにしました。同期するとGoogleカレンダーへ反映されます。";
        await SyncAfterLocalChangeAsync();
    }

    public async Task SetTodoQuickFilterAsync(TodoQuickFilter filter)
    {
        TodoQuickFilter = filter;
        _showAllIncompleteTodos = false;
        await RefreshTodosAsync();
    }

    public async Task UpdateSelectedTodoAsync(string? priority = null, int? progressDelta = null)
    {
        if (SelectedEvent is not { IsTodoLike: true } todoEvent || todoEvent.IsTodoDone)
        {
            return;
        }

        var originalTodo = CloneEventForEditing(todoEvent);
        var updatedTodo = CloneEventForEditing(todoEvent);
        var metadata = updatedTodo.TodoMetadata;
        var nextPriority = string.IsNullOrWhiteSpace(priority) ? metadata?.Priority ?? "A" : priority;
        var nextProgress = Math.Clamp((metadata?.Progress ?? 0) + (progressDelta ?? 0), 0, 100);
        updatedTodo.Description = TagService.UpdateTodoMarker(updatedTodo.Description, nextPriority, nextProgress);
        updatedTodo.IsDirty = true;
        await _repository.SaveEventAsync(updatedTodo);
        CaptureUndo("ToDo更新", [originalTodo]);
        await RefreshCalendarAsync();
        SelectedEvent = _visibleEvents.FirstOrDefault(item => item.Id == updatedTodo.Id) ?? updatedTodo;
        Status = $"ToDoを更新しました: 優先度 {nextPriority} / 進捗 {nextProgress}%";
        await SyncAfterLocalChangeAsync();
    }

    private async Task RefreshTodosAsync()
    {
        TodoEvents.Clear();
        CompletedTodoEvents.Clear();
        var events = (await _repository.LoadTodoEventsAsync()).Where(IsVisible).ToArray();
        ApplyDisplayColors(events);

        var incompleteTodos = events
            .Where(item => !item.IsTodoDone && TodoDisplayFilter.IsWithinDisplayPeriod(item, _settings.IncompleteTodoDisplayPeriodMonths, DateTime.Today))
            .Where(PassesTodoQuickFilter)
            .OrderBy(item => item.Start)
            .ThenBy(item => item.TodoPriority)
            .ToArray();
        _incompleteTodosTotalCount = incompleteTodos.Length;
        foreach (var item in incompleteTodos.Take(_showAllIncompleteTodos ? incompleteTodos.Length : TodoListDisplayLimit))
        {
            TodoEvents.Add(item);
        }

        var completedTodos = events
            .Where(item => item.IsTodoDone && TodoDisplayFilter.IsWithinDisplayPeriod(item, _settings.CompletedTodoDisplayPeriodMonths, DateTime.Today))
            .OrderBy(item => Math.Abs((item.Start.Date - DateTime.Today).Days))
            .ThenBy(item => item.Start.Date)
            .ThenByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Title, StringComparer.CurrentCulture)
            .ToArray();
        _completedTodosTotalCount = completedTodos.Length;
        foreach (var item in completedTodos.Take(_showAllCompletedTodos ? completedTodos.Length : TodoListDisplayLimit))
        {
            CompletedTodoEvents.Add(item);
        }

        OnPropertyChanged(nameof(TodoEventsRemainingCount));
        OnPropertyChanged(nameof(CompletedTodoEventsRemainingCount));
        OnPropertyChanged(nameof(TodoEventsCountText));
        OnPropertyChanged(nameof(CompletedTodoEventsCountText));
        OnPropertyChanged(nameof(TodoEventsLimitButtonText));
        OnPropertyChanged(nameof(CompletedTodoEventsLimitButtonText));
        OnPropertyChanged(nameof(IsTodoEventsLimitControlVisible));
        OnPropertyChanged(nameof(IsCompletedTodoEventsLimitControlVisible));
    }

    private bool PassesTodoQuickFilter(CalendarEvent item)
    {
        var today = DateTime.Today;
        return TodoQuickFilter switch
        {
            TodoQuickFilter.Today => item.Start.Date == today,
            TodoQuickFilter.Overdue => item.Start.Date < today,
            TodoQuickFilter.ThisWeek => item.Start.Date >= today && item.Start.Date < today.AddDays(7),
            TodoQuickFilter.HighPriority => string.Equals(item.TodoPriority, "A", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(item.TodoPriority, "B", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }
}
