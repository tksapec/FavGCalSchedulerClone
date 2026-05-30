using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarEventDisplayTests
{
    [Fact]
    public void CalendarCellDisplayText_IncludesTimeForTimedEvent()
    {
        var item = new CalendarEvent
        {
            Title = "Meeting",
            Start = new DateTimeOffset(new DateTime(2026, 5, 23, 9, 30, 0)),
            End = new DateTimeOffset(new DateTime(2026, 5, 23, 10, 0, 0))
        };

        Assert.Equal("09:30 Meeting", item.CalendarCellDisplayText);
    }

    [Fact]
    public void CalendarCellDisplayText_ShowsOnlyTodoTitle()
    {
        var item = new CalendarEvent
        {
            Title = "Confirm",
            Description = "#todoA56%",
            IsAllDay = true,
            IsTodoLike = true,
            Start = new DateTimeOffset(new DateTime(2026, 5, 23))
        };

        Assert.Equal("Confirm", item.CalendarCellDisplayText);
    }

    [Fact]
    public void IsOverdueTodo_ReturnsTrueForIncompleteTodoBeforeToday()
    {
        var item = Todo(DateTime.Today.AddDays(-1), 56);

        Assert.True(item.IsOverdueTodo);
    }

    [Fact]
    public void IsOverdueTodo_ReturnsFalseForIncompleteTodoDueToday()
    {
        var item = Todo(DateTime.Today, 56);

        Assert.False(item.IsOverdueTodo);
    }

    [Fact]
    public void IsOverdueTodo_ReturnsFalseForIncompleteTodoAfterToday()
    {
        var item = Todo(DateTime.Today.AddDays(1), 56);

        Assert.False(item.IsOverdueTodo);
    }

    [Fact]
    public void IsOverdueTodo_ReturnsFalseForCompletedTodoBeforeToday()
    {
        var item = Todo(DateTime.Today.AddDays(-1), 100);

        Assert.False(item.IsOverdueTodo);
    }

    [Fact]
    public void IsOverdueTodo_ReturnsFalseForNormalEventBeforeToday()
    {
        var item = new CalendarEvent
        {
            Title = "Past meeting",
            IsTodoLike = false,
            Start = new DateTimeOffset(DateTime.Today.AddDays(-1)),
            End = new DateTimeOffset(DateTime.Today)
        };

        Assert.False(item.IsOverdueTodo);
    }

    private static CalendarEvent Todo(DateTime dueDate, int progress) => new()
    {
        Title = "Todo",
        Description = $"#todoA{progress}%",
        IsAllDay = true,
        IsTodoLike = true,
        Start = new DateTimeOffset(dueDate.Date),
        End = new DateTimeOffset(dueDate.Date.AddDays(1))
    };
}
