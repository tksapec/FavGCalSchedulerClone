using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    public bool HasShownTrayCloseNotification
    {
        get
        {
            lock (_settingsStateLock)
            {
                return _settings.HasShownTrayCloseNotification;
            }
        }
    }

    public async Task MarkTrayCloseNotificationShownAsync()
    {
        SettingsPersistenceRequest snapshot;
        lock (_settingsStateLock)
        {
            if (_settings.HasShownTrayCloseNotification)
            {
                return;
            }

            _settings.HasShownTrayCloseNotification = true;
            snapshot = CreateSettingsPersistenceRequestUnsafe();
        }

        try
        {
            await PersistSettingsAsync(snapshot);
        }
        catch
        {
            lock (_settingsStateLock)
            {
                if (_settingsRevision == snapshot.Revision)
                {
                    _settings.HasShownTrayCloseNotification = false;
                }
            }

            throw;
        }
    }
}
