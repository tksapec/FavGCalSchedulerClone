namespace FavGCalSchedulerClone.App.Services;

internal readonly record struct NewScheduleTimeRange(DateTime Start, DateTime End);

internal static class NewScheduleTimeDefaults
{
    public static NewScheduleTimeRange Create(DateTime now)
    {
        var rounded = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);
        if (now.Second != 0 || now.Millisecond != 0 || rounded.Minute % 30 != 0)
        {
            rounded = rounded.AddMinutes(30 - rounded.Minute % 30);
        }

        return new NewScheduleTimeRange(rounded, rounded.AddHours(1));
    }
}
