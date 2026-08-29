using FavGCalSchedulerClone.App.Commands;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    private AsyncRelayCommand? _toggleReturnToTodayWhenDeactivatedCommand;

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

    public AsyncRelayCommand ToggleReturnToTodayWhenDeactivatedCommand =>
        _toggleReturnToTodayWhenDeactivatedCommand ??=
            CreateAsyncCommand(ToggleReturnToTodayWhenDeactivatedAsync);

    internal async Task ToggleReturnToTodayWhenDeactivatedAsync()
    {
        SettingsPersistenceRequest snapshot;
        bool previous;
        bool enabled;
        lock (_settingsStateLock)
        {
            previous = _settings.ReturnToTodayWhenDeactivated;
            enabled = !previous;
            _settings.ReturnToTodayWhenDeactivated = enabled;
            snapshot = CreateSettingsPersistenceRequestUnsafe();
        }

        OnPropertyChanged(nameof(ReturnToTodayWhenDeactivated));
        try
        {
            await PersistSettingsAsync(snapshot);
        }
        catch
        {
            var restored = false;
            lock (_settingsStateLock)
            {
                if (_settingsRevision == snapshot.Revision
                    && _settings.ReturnToTodayWhenDeactivated == enabled)
                {
                    _settings.ReturnToTodayWhenDeactivated = previous;
                    restored = true;
                }
            }

            if (restored)
            {
                OnPropertyChanged(nameof(ReturnToTodayWhenDeactivated));
            }

            throw;
        }

        Status = enabled
            ? "フォーカス解除時に今日へ戻す機能を有効にしました。"
            : "フォーカス解除時に今日へ戻す機能を無効にしました。";
    }
}
