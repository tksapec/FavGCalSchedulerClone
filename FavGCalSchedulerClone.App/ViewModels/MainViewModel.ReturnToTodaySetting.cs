namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    public bool ReturnToTodayWhenDeactivated
    {
        get
        {
            lock (_settingsStateLock)
            {
                return _settings.ReturnToTodayWhenDeactivated;
            }
        }
    }
}
