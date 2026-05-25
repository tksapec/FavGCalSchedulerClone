using System.Collections.ObjectModel;
using System.ComponentModel;

namespace FavGCalSchedulerClone.App.Models;

public sealed class CalendarDay : INotifyPropertyChanged
{
    private bool _isDropTarget;

    public DateTime Date { get; init; }
    public bool IsCurrentMonth { get; init; }
    public bool IsToday => Date.Date == DateTime.Today;
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public bool IsSunday => Date.DayOfWeek == DayOfWeek.Sunday;
    public bool IsSaturday => Date.DayOfWeek == DayOfWeek.Saturday;
    public bool IsHoliday { get; set; }
    public bool IsWorkdayOverride { get; set; }
    public ObservableCollection<CalendarEvent> Events { get; } = [];
    public ObservableCollection<CalendarEventSegment> Segments { get; } = [];
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget == value)
            {
                return;
            }

            _isDropTarget = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDropTarget)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
