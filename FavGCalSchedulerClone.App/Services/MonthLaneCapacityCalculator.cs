namespace FavGCalSchedulerClone.App.Services;

public static class MonthLaneCapacityCalculator
{
    public const int DefaultCapacity = CalendarSegmentLayoutService.MaxLanes;
    public const double CellTopInset = 1;
    public const double CellBottomInset = 1;

    public static int Calculate(double cellHeight, double calendarLabelFontSize)
    {
        if (double.IsNaN(cellHeight) || double.IsInfinity(cellHeight) || cellHeight <= 0)
        {
            return DefaultCapacity;
        }

        var eventBarPitch = Math.Max(16, calendarLabelFontSize + 3);
        var availableEventHeight = cellHeight - CellTopInset - CellBottomInset;
        return Math.Max(CalendarSegmentLayoutService.MinimumLanes, (int)Math.Floor(availableEventHeight / eventBarPitch));
    }
}
