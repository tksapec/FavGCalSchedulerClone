using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Repositories;

public interface ISettingsRepository
{
    Task<AppSettings> LoadSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    Task<string?> LoadSettingValueAsync(string key);
    Task SaveSettingValueAsync(string key, string? value);
}
