using System.Collections.ObjectModel;
using System.ComponentModel;

namespace FavGCalSchedulerClone.App.Models;

public sealed class CalendarDay : INotifyPropertyChanged
{
    private DateTime _date;
    private bool _isCurrentMonth;
    private bool _isDropTarget;
    private bool _isHoliday;
    private bool _isWorkdayOverride;

    public DateTime Date
    {
        get => _date;
        set
        {
            if (_date == value)
            {
                return;
            }

            _date = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Date)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsToday)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWeekend)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSunday)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSaturday)));
        }
    }

    public bool IsCurrentMonth
    {
        get => _isCurrentMonth;
        set
        {
            if (_isCurrentMonth == value)
            {
                return;
            }

            _isCurrentMonth = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrentMonth)));
        }
    }
    public bool IsToday => Date.Date == DateTime.Today;
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public bool IsSunday => Date.DayOfWeek == DayOfWeek.Sunday;
    public bool IsSaturday => Date.DayOfWeek == DayOfWeek.Saturday;
    public bool IsHoliday
    {
        get => _isHoliday;
        set
        {
            if (_isHoliday == value)
            {
                return;
            }

            _isHoliday = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHoliday)));
        }
    }

    public bool IsWorkdayOverride
    {
        get => _isWorkdayOverride;
        set
        {
            if (_isWorkdayOverride == value)
            {
                return;
            }

            _isWorkdayOverride = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWorkdayOverride)));
        }
    }
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
