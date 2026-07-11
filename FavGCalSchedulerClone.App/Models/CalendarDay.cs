using System.Collections.ObjectModel;
using System.ComponentModel;
using FavGCalSchedulerClone.App.Collections;

namespace FavGCalSchedulerClone.App.Models;

public sealed class CalendarDay : INotifyPropertyChanged
{
    private DateTime _date;
    private bool _isCurrentMonth;
    private bool _isDropTarget;
    private bool _isHoliday;
    private bool _isWorkdayOverride;
    private int _hiddenEventCount;
    private readonly BulkObservableCollection<CalendarEvent> _events = [];
    private readonly BulkObservableCollection<CalendarEventSegment> _segments = [];

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
    public ObservableCollection<CalendarEvent> Events => _events;
    public ObservableCollection<CalendarEventSegment> Segments => _segments;

    public void ReplaceEvents(IEnumerable<CalendarEvent> events) => _events.ReplaceAll(events);

    public void ReplaceSegments(IEnumerable<CalendarEventSegment> segments) => _segments.ReplaceAll(segments);
    public int HiddenEventCount
    {
        get => _hiddenEventCount;
        set
        {
            if (_hiddenEventCount == value)
            {
                return;
            }

            _hiddenEventCount = Math.Max(0, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HiddenEventCount)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHiddenEvents)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HiddenEventText)));
        }
    }

    public bool HasHiddenEvents => HiddenEventCount > 0;
    public string HiddenEventText => HiddenEventCount > 0 ? $"+{HiddenEventCount}件" : "";

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
