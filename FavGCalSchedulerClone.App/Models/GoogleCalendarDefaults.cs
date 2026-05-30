namespace FavGCalSchedulerClone.App.Models;

public static class GoogleCalendarDefaults
{
    public const string PrimaryCalendarId = "primary";
    public const string CalendarEventsScope = "https://www.googleapis.com/auth/calendar.events";
    public const string CalendarListReadonlyScope = "https://www.googleapis.com/auth/calendar.calendarlist.readonly";
    public static readonly string[] CalendarScopes = [CalendarEventsScope, CalendarListReadonlyScope];
}
