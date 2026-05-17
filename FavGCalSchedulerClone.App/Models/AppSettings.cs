namespace FavGCalSchedulerClone.App.Models;

public sealed class AppSettings
{
    public string? OAuthClientJsonPath { get; set; }
    public string ActiveCalendarId { get; set; } = GoogleCalendarDefaults.PrimaryCalendarId;
    public List<string> VisibleCalendarIds { get; set; } = [];
    public DateTime DisplayMonth { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public int StartupTabIndex { get; set; }
    public bool ConfirmBeforeDelete { get; set; } = true;
    public bool CloseButtonExitsApplication { get; set; } = true;
    public bool DefaultNewEventIsAllDay { get; set; } = true;
    public bool UseWindowsToastNotifications { get; set; } = true;
}
