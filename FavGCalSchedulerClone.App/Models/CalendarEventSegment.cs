using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FavGCalSchedulerClone.App.Models;

public sealed class CalendarEventSegment : INotifyPropertyChanged
{
    private bool _isSelected;

    public CalendarEvent? Event { get; init; }
    public DateTime Date { get; init; }
    public int Lane { get; init; }
    public bool IsWeekSegmentStart { get; init; }
    public bool IsWeekSegmentEnd { get; init; }
    public bool ShowText { get; init; }
    public bool IsVisible => Event is not null;
    public string DisplayColor => ResolveColors().Background;
    public string DisplayForegroundColor => ResolveColors().Foreground;
    public string DisplayText => ShowText ? Event?.CalendarCellDisplayText ?? "" : "";
    public string? ToolTipText => Event?.ToolTipText;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayColor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayForegroundColor)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static CalendarEventSegment Empty(DateTime date, int lane) => new()
    {
        Date = date,
        Lane = lane
    };

    private EventDisplayColors ResolveColors()
    {
        if (Event is null)
        {
            return new EventDisplayColors("Transparent", "Transparent");
        }

        return IsSelected
            ? FavGCalSchedulerClone.App.Services.TagService.ResolveSelectedDisplayColors(Event.DisplayColor, Event.DisplayForegroundColor)
            : new EventDisplayColors(Event.DisplayColor, Event.DisplayForegroundColor);
    }
}
