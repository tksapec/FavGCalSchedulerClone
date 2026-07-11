namespace FavGCalSchedulerClone.App.Services;

public static class MonthLaneCapacityCalculator
{
    public const int DefaultCapacity = CalendarSegmentLayoutService.MaxLanes;
    public const double DateHeaderHeight = 23;
    public const double EventAreaTopMargin = 3;
    public const double OverflowFooterHeight = 16;

    public static int Calculate(double cellHeight, double calendarLabelFontSize)
    {
        if (double.IsNaN(cellHeight) || double.IsInfinity(cellHeight) || cellHeight <= 0)
        {
            return DefaultCapacity;
        }

        var eventBarPitch = Math.Max(17, calendarLabelFontSize + 2);
        var availableEventHeight = cellHeight - DateHeaderHeight - EventAreaTopMargin - OverflowFooterHeight;
        return Math.Max(CalendarSegmentLayoutService.MinimumLanes, (int)Math.Floor(availableEventHeight / eventBarPitch));
    }
}
