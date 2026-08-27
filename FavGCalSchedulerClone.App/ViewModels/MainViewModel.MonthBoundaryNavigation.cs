namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    private DateTime GetDisplayedMonthNavigationTarget(DateTime anchor, int direction)
    {
        var targetMonth = CurrentMonth.AddMonths(direction);
        var day = Math.Min(anchor.Day, DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month));
        return new DateTime(targetMonth.Year, targetMonth.Month, day);
    }
}
