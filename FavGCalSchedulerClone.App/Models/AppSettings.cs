namespace FavGCalSchedulerClone.App.Models;

public sealed class AppSettings
{
    public string? OAuthClientJsonPath { get; set; }
    public string ActiveCalendarId { get; set; } = GoogleCalendarDefaults.PrimaryCalendarId;
    public DateTime DisplayMonth { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
}
