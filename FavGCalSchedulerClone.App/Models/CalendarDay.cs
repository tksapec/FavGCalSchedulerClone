using System.Collections.ObjectModel;

namespace FavGCalSchedulerClone.App.Models;

public sealed class CalendarDay
{
    public DateTime Date { get; init; }
    public bool IsCurrentMonth { get; init; }
    public bool IsToday => Date.Date == DateTime.Today;
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public bool IsSunday => Date.DayOfWeek == DayOfWeek.Sunday;
    public bool IsSaturday => Date.DayOfWeek == DayOfWeek.Saturday;
    public bool IsHoliday { get; set; }
    public bool IsWorkdayOverride { get; set; }
    public string? TagBackgroundColor { get; set; }
    public bool HasTagBackgroundColor => !string.IsNullOrWhiteSpace(TagBackgroundColor);
    public ObservableCollection<CalendarEvent> Events { get; } = [];
}
