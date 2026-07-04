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
            ColorId = EditorColorId
        };

        await _repository.SaveEventAsync(todoEvent);
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
        CaptureUndo("ToDo編集", [originalTodo]);
        editingTodo.Title = title.Trim();
        editingTodo.Description = TagService.UpdateTodoMarker(description, priority, progress);
        editingTodo.CalendarId = ResolveEditorCalendarId();
        editingTodo.Start = new DateTimeOffset(dueDate.Date);
        editingTodo.End = new DateTimeOffset(dueDate.Date.AddDays(1));
        editingTodo.IsAllDay = true;
        editingTodo.IsDirty = true;
        editingTodo.IsDeleted = false;
        editingTodo.IsTodoLike = true;
        editingTodo.ColorId = EditorColorId;

        await SaveEventWithCalendarMoveAsync(editingTodo, originalTodo);
        await RefreshCalendarAsync();
        SelectedEvent = _visibleEvents.FirstOrDefault(item => item.Id == editingTodo.Id) ?? editingTodo;
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

        var priority = SelectedEvent.TodoPriority;
        CaptureUndo("ToDo完了", [SelectedEvent]);
        SelectedEvent.Description = TagService.UpdateTodoMarker(SelectedEvent.Description, priority, 100);
        SelectedEvent.IsDirty = true;
        await _repository.SaveEventAsync(SelectedEvent);
        await RefreshCalendarAsync();
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

        var priority = todoEvent.TodoPriority;
        CaptureUndo("ToDo完了", [todoEvent]);
        todoEvent.Description = TagService.UpdateTodoMarker(todoEvent.Description, priority, 100);
        todoEvent.IsDirty = true;
        await _repository.SaveEventAsync(todoEvent);
        await RefreshCalendarAsync();
        MarkSelectedTodoDoneCommand.RaiseCanExecuteChanged();
        Status = "ToDoを処理済みにしました。同期するとGoogleカレンダーへ反映されます。";
        await SyncAfterLocalChangeAsync();
    }

    public async Task SetTodoQuickFilterAsync(TodoQuickFilter filter)
    {
        TodoQuickFilter = filter;
        await RefreshTodosAsync();
    }

    public async Task UpdateSelectedTodoAsync(string? priority = null, int? progressDelta = null)
    {
        if (SelectedEvent is not { IsTodoLike: true } todoEvent || todoEvent.IsTodoDone)
        {
            return;
        }

        var metadata = todoEvent.TodoMetadata;
        CaptureUndo("ToDo更新", [todoEvent]);
        var nextPriority = string.IsNullOrWhiteSpace(priority) ? metadata?.Priority ?? "A" : priority;
        var nextProgress = Math.Clamp((metadata?.Progress ?? 0) + (progressDelta ?? 0), 0, 100);
        todoEvent.Description = TagService.UpdateTodoMarker(todoEvent.Description, nextPriority, nextProgress);
        todoEvent.IsDirty = true;
        await _repository.SaveEventAsync(todoEvent);
        await RefreshCalendarAsync();
        SelectedEvent = _visibleEvents.FirstOrDefault(item => item.Id == todoEvent.Id) ?? todoEvent;
        Status = $"ToDoを更新しました: 優先度 {nextPriority} / 進捗 {nextProgress}%";
        await SyncAfterLocalChangeAsync();
    }

    private async Task RefreshTodosAsync()
    {
        TodoEvents.Clear();
        CompletedTodoEvents.Clear();
        var events = (await _repository.LoadTodoEventsAsync()).Where(IsVisible).ToArray();
        ApplyDisplayColors(events);

        foreach (var item in events
                     .Where(item => !item.IsTodoDone && TodoDisplayFilter.IsWithinDisplayPeriod(item, _settings.IncompleteTodoDisplayPeriodMonths, DateTime.Today))
                     .Where(PassesTodoQuickFilter)
                     .OrderBy(item => item.Start)
                     .ThenBy(item => item.TodoPriority)
                     .Take(100))
        {
            TodoEvents.Add(item);
        }

        foreach (var item in events
                     .Where(item => item.IsTodoDone && TodoDisplayFilter.IsWithinDisplayPeriod(item, _settings.CompletedTodoDisplayPeriodMonths, DateTime.Today))
                     .OrderBy(item => Math.Abs((item.Start.Date - DateTime.Today).Days))
                     .ThenBy(item => item.Start.Date)
                     .ThenByDescending(item => item.UpdatedAt)
                     .ThenBy(item => item.Title, StringComparer.CurrentCulture)
                     .Take(100))
        {
            CompletedTodoEvents.Add(item);
        }
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
