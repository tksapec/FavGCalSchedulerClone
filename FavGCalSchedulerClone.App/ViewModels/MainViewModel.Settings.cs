using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FavGCalSchedulerClone.App.Commands;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Win32;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{

    public AppSettings CreateSettingsSnapshot()
    {
        lock (_settingsStateLock)
        {
            return CreateSettingsPersistenceSnapshotUnsafe();
        }
    }

    public async Task SaveApplicationSettingsAsync(AppSettings settings)
    {
        SettingsPersistenceRequest snapshot;
        lock (_settingsStateLock)
        {
            var displayMonth = _settings.DisplayMonth;
            // The settings dialog retains its own mutable instance. Clone it before
            // normalization so later dialog edits cannot mutate the ViewModel state.
            _settings = AppSettingsNormalizer.Normalize(DeepCloneSettings(settings));
            _settings.DisplayMonth = displayMonth;
            snapshot = CreateSettingsPersistenceRequestUnsafe();
        }
        SelectedTabIndex = _settings.StartupTabIndex;
        SelectedTodoTabIndex = _settings.StartupTodoTabIndex;
        CurrentViewMode = _settings.StartupCalendarViewMode;
        await PersistSettingsAsync(snapshot);

        foreach (var propertyName in new[]
        {
            nameof(StartupTabIndex), nameof(StartupCalendarViewMode), nameof(ConfirmBeforeDelete),
            nameof(DefaultNewEventIsAllDay), nameof(HideMainWindowWhileEditingSchedule), nameof(ReuseLastScheduleInput),
            nameof(DefaultScheduleReminderMinutes), nameof(CalendarLabelFontSize),
            nameof(SideListFontSize), nameof(WindowOpacity), nameof(WeekdayHeaders),
            nameof(EnableReminderSound), nameof(ReminderSoundFilePath),
            nameof(ReminderSoundVolume), nameof(EventColorOptions)
        })
        {
            OnPropertyChanged(propertyName);
        }

        await RefreshCalendarAsync();
        Status = "アプリ設定を保存しました。";
    }

    public async Task<IReadOnlyList<string>> LoadScheduleTitleHistoryAsync()
    {
        await ReloadScheduleHistoryAsync();
        return _scheduleTitleHistory;
    }

    public async Task<IReadOnlyList<string>> LoadScheduleLocationHistoryAsync()
    {
        await ReloadScheduleHistoryAsync();
        return _scheduleLocationHistory;
    }

    public async Task ClearScheduleTitleHistoryAsync()
    {
        await _repository.SaveSettingValueAsync(ScheduleTitleHistoryKey, null);
        _scheduleTitleHistory = [];
    }

    public async Task ClearScheduleLocationHistoryAsync()
    {
        await _repository.SaveSettingValueAsync(ScheduleLocationHistoryKey, null);
        _scheduleLocationHistory = [];
    }

    public async Task SetOAuthClientJsonPathAsync(string path)
    {
        OAuthClientJsonPath = path;
        SettingsPersistenceRequest snapshot;
        lock (_settingsStateLock)
        {
            _settings.OAuthClientJsonPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            snapshot = CreateSettingsPersistenceRequestUnsafe();
        }
        await PersistSettingsAsync(snapshot);
        await ReloadAvailableCalendarsAsync();
    }

    public async Task AuthorizeGoogleAsync()
    {
        await AuthorizeAsync();
    }

    public void SetWindowCommandHandlers(
        Func<Task> showAddScheduleAsync,
        Func<Task> showAddTodoAsync,
        Func<Task> backupAllCalendarsAsync,
        Func<Task> restoreAllCalendarsAsync,
        Func<Task> importFavGCalSchedulerAsync,
        Func<Task> importCsvAsync,
        Func<Task> exportCsvAsync,
        Func<Task> showScheduleListAsync,
        Func<Task> showSearchAsync,
        Func<Task> showSyncDiagnosticsAsync,
        Func<Task> showSettingsAsync,
        Func<Task> showReminderHistoryAsync,
        Func<Task> showAboutAsync,
        Func<Task>? showMonthJumpAsync = null)
    {
        _showAddScheduleAsync = showAddScheduleAsync;
        _showAddTodoAsync = showAddTodoAsync;
        _backupAllCalendarsAsync = backupAllCalendarsAsync;
        _restoreAllCalendarsAsync = restoreAllCalendarsAsync;
        _importFavGCalSchedulerAsync = importFavGCalSchedulerAsync;
        _importCsvAsync = importCsvAsync;
        _exportCsvAsync = exportCsvAsync;
        _showScheduleListAsync = showScheduleListAsync;
        _showSearchAsync = showSearchAsync;
        _showSyncDiagnosticsAsync = showSyncDiagnosticsAsync;
        _showSettingsAsync = showSettingsAsync;
        _showReminderHistoryAsync = showReminderHistoryAsync;
        _showAboutAsync = showAboutAsync;
        _showMonthJumpAsync = showMonthJumpAsync;
    }

    private static Task InvokeWindowCommandAsync(Func<Task>? handler) => handler?.Invoke() ?? Task.CompletedTask;

    private async Task RecordScheduleHistoryAsync(CalendarEvent calendarEvent)
    {
        _scheduleTitleHistory = AddHistoryValue(_scheduleTitleHistory, calendarEvent.Title);
        _scheduleLocationHistory = AddHistoryValue(_scheduleLocationHistory, calendarEvent.Location);
        await _repository.SaveSettingValueAsync(ScheduleTitleHistoryKey, JsonSerializer.Serialize(_scheduleTitleHistory));
        await _repository.SaveSettingValueAsync(ScheduleLocationHistoryKey, JsonSerializer.Serialize(_scheduleLocationHistory));
    }

    private async Task BrowseOAuthClientAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Google OAuth client JSON (*.json)|*.json|All files (*.*)|*.*",
            Title = "Desktop OAuth client JSONを選択"
        };

        if (dialog.ShowDialog() == true)
        {
            OAuthClientJsonPath = dialog.FileName;
            SettingsPersistenceRequest snapshot;
            lock (_settingsStateLock)
            {
                _settings.OAuthClientJsonPath = dialog.FileName;
                snapshot = CreateSettingsPersistenceRequestUnsafe();
            }
            await PersistSettingsAsync(snapshot);
            await ReloadAvailableCalendarsAsync();
            Status = "OAuth client JSONを保存しました。";
        }
    }

    private async Task AuthorizeAsync()
    {
        await SaveOAuthPathAsync();
        var settings = CreateSettingsSnapshot();
        if (string.IsNullOrWhiteSpace(settings.OAuthClientJsonPath))
        {
            Status = "先にOAuth client JSONを設定してください。";
            return;
        }

        Status = "ブラウザーでGoogle認証を続行してください。";
        await _syncService.AuthorizeAsync(settings.OAuthClientJsonPath);
        _eventColorPalette = await _syncService.RefreshEventColorPaletteAsync();
        await ReloadAvailableCalendarsAsync();
        await RefreshCalendarAsync();
        Status = "Google認証が完了しました。";
    }

    public async Task ClearTokensAsync()
    {
        await _syncService.ClearTokensAsync();
        Status = "保存済みGoogleトークンを削除しました。";
    }

    private async Task SaveOAuthPathAsync()
    {
        SettingsPersistenceRequest snapshot;
        lock (_settingsStateLock)
        {
            _settings.OAuthClientJsonPath = string.IsNullOrWhiteSpace(OAuthClientJsonPath) ? null : OAuthClientJsonPath.Trim();
            _settings.VisibleCalendarIds = AvailableCalendars.Where(item => item.IsSelected).Select(item => item.Id).ToList();
            _settings.ActiveCalendarId = ResolveEditorCalendarId();
            snapshot = CreateSettingsPersistenceRequestUnsafe();
        }
        await PersistSettingsAsync(snapshot);
    }

    private async Task<AppSettings> LoadSettingsSafelyAsync()
    {
        try
        {
            return await _repository.LoadSettingsAsync();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            Debug.WriteLine(ex);
            _logger?.LogError(ex, "Stored application settings are invalid. Default settings will be used.");
            return new AppSettings();
        }
    }

    private AppSettings CreateSettingsPersistenceSnapshot()
    {
        lock (_settingsStateLock)
        {
            return CreateSettingsPersistenceSnapshotUnsafe();
        }
    }

    private AppSettings CreateSettingsPersistenceSnapshotUnsafe()
        => DeepCloneSettings(_settings);

    // Call only while _settingsStateLock is held and after a state mutation.
    // The revision makes a queued, older snapshot unable to overwrite a newer
    // interactive settings save after it obtains the persistence gate.
    private SettingsPersistenceRequest CreateSettingsPersistenceRequestUnsafe()
        => new(DeepCloneSettings(_settings), ++_settingsRevision);

    private static AppSettings DeepCloneSettings(AppSettings settings)
        => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings)) ?? new AppSettings();

    private async Task PersistSettingsAsync(SettingsPersistenceRequest request)
    {
        await _settingsPersistenceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_settingsStateLock)
            {
                if (request.Revision <= _persistedSettingsRevision)
                {
                    return;
                }
            }

            await _repository.SaveSettingsAsync(request.Settings).ConfigureAwait(false);
            lock (_settingsStateLock)
            {
                _persistedSettingsRevision = Math.Max(_persistedSettingsRevision, request.Revision);
            }
        }
        finally
        {
            _settingsPersistenceGate.Release();
        }
    }

    private sealed record SettingsPersistenceRequest(AppSettings Settings, long Revision);

    private static IReadOnlyList<string> DeserializeHistory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return (JsonSerializer.Deserialize<List<string?>>(json) ?? [])
                .OfType<string>()
                .ToArray();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            Debug.WriteLine(ex);
            return [];
        }
    }

    private static IReadOnlyList<string> AddHistoryValue(IReadOnlyList<string> history, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return history;
        }

        return new[] { value.Trim() }
            .Concat(history.Where(item => !string.Equals(item, value.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Take(50)
            .ToArray();
    }
}
