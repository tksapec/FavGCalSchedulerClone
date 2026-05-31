namespace FavGCalSchedulerClone.App.Repositories;

public interface ISyncStateRepository
{
    Task<string?> GetSyncTokenAsync(string calendarId);
    Task SaveSyncTokenAsync(string calendarId, string? syncToken);
}
