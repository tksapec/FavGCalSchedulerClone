using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.Tests;

public sealed class GoogleCalendarDefaultsTests
{
    [Fact]
    public void CalendarScopes_IncludeEventSyncAndCalendarListReadScopes()
    {
        Assert.Contains(GoogleCalendarDefaults.CalendarEventsScope, GoogleCalendarDefaults.CalendarScopes);
        Assert.Contains(GoogleCalendarDefaults.CalendarListReadonlyScope, GoogleCalendarDefaults.CalendarScopes);
    }
}
