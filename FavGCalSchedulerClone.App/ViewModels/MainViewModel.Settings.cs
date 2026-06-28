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

    public async Task SaveApplicationSettingsAsync(
        int startupTabIndex,
        bool confirmBeforeDelete,
        bool closeButtonExitsApplication,
        bool defaultNewEventIsAllDay)
    {
        _settings.StartupTabIndex = AppSettingsNormalizer.NormalizeTabIndex(startupTabIndex);
        _settings.ConfirmBeforeDelete = confirmBeforeDelete;
        _settings.CloseButtonExitsApplication = closeButtonExitsApplication;
        _settings.DefaultNewEventIsAllDay = defaultNewEventIsAllDay;
        await SaveApplicationSettingsAsync(_settings);
    }

    public AppSettings CreateSettingsSnapshot()
    {
        return JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_settings)) ?? new AppSettings();
    }

    public async Task SaveApplicationSettingsAsync(AppSettings settings)
    {
        _settings = AppSettingsNormalizer.Normalize(settings);
        SelectedTabIndex = _settings.StartupTabIndex;
        SelectedTodoTabIndex = _settings.StartupTodoTabIndex;
        CurrentViewMode = _settings.StartupCalendarViewMode;
        await _repository.SaveSettingsAsync(_settings);

        foreach (var propertyName in new[]
        {
            nameof(StartupTabIndex), nameof(StartupCalendarViewMode), nameof(ConfirmBeforeDelete),
            nameof(CloseButtonExitsApplication), nameof(DefaultNewEventIsAllDay),
            nameof(HideMainWindowWhileEditingSchedule), nameof(ReuseLastScheduleInput),
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
        _settings.OAuthClientJsonPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        await _repository.SaveSettingsAsync(_settings);
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
            _settings.OAuthClientJsonPath = dialog.FileName;
            await _repository.SaveSettingsAsync(_settings);
            await ReloadAvailableCalendarsAsync();
            Status = "OAuth client JSONを保存しました。";
        }
    }

    private async Task AuthorizeAsync()
    {
        await SaveOAuthPathAsync();
        if (string.IsNullOrWhiteSpace(_settings.OAuthClientJsonPath))
        {
            Status = "先にOAuth client JSONを設定してください。";
            return;
        }

        Status = "ブラウザーでGoogle認証を続行してください。";
        await _syncService.AuthorizeAsync(_settings.OAuthClientJsonPath);
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
        _settings.OAuthClientJsonPath = string.IsNullOrWhiteSpace(OAuthClientJsonPath) ? null : OAuthClientJsonPath.Trim();
        _settings.VisibleCalendarIds = AvailableCalendars.Where(item => item.IsSelected).Select(item => item.Id).ToList();
        _settings.ActiveCalendarId = ResolveEditorCalendarId();
        await _repository.SaveSettingsAsync(_settings);
    }

    private static IReadOnlyList<string> DeserializeHistory(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json) ?? [];
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
