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

    private EventColorSelectionItem? ApplyEventColorSetting(EventColorSelectionItem item)
    {
        if (item.Id is null)
        {
            return item;
        }

        var setting = _settings.EventColorSettings.FirstOrDefault(setting => setting.ColorId == item.Id);
        if (setting?.IsEnabled == false)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(setting?.Label)
            ? item
            : item with { Label = setting.Label! };
    }

    public async Task SaveTagsAsync()
    {
        await CalendarTagAtomicWriter.SaveTagsAsync(_repository, Tags);
        await RefreshCalendarAsync();
        Status = "タグ設定を保存しました。";
    }

    private async Task ReloadTagsAsync()
    {
        Tags.Clear();
        foreach (var tag in await _repository.LoadTagsAsync())
        {
            Tags.Add(tag);
        }
    }

    public Task ReloadAvailableCalendarsAsync() =>
        RunExclusiveSyncDataOperationAsync(ReloadAvailableCalendarsCoreAsync);

    private async Task ReloadAvailableCalendarsCoreAsync()
    {
        var calendars = await LoadAvailableCalendarsCoreAsync();

        AvailableCalendars.Clear();
        foreach (var calendar in calendars)
        {
            AvailableCalendars.Add(calendar);
        }

        RefreshCalendarNames();
        if (!AvailableCalendars.Any(item => item.IsSelected) && AvailableCalendars.Count > 0)
        {
            AvailableCalendars[0].IsSelected = true;
        }

        EditorCalendarId = ResolveEditorCalendarId();
    }

    public async Task ApplyCalendarSelectionAsync()
    {
        if (Interlocked.Exchange(ref _calendarSelectionInProgress, 1) != 0)
        {
            Interlocked.Exchange(ref _calendarSelectionRerunRequested, 1);
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _calendarSelectionRerunRequested, 0);
                await ApplyCalendarSelectionCoreAsync();
            }
            while (Interlocked.Exchange(ref _calendarSelectionRerunRequested, 0) != 0);
        }
        finally
        {
            Interlocked.Exchange(ref _calendarSelectionInProgress, 0);
        }
    }

    private async Task ApplyCalendarSelectionCoreAsync()
    {
        if (!AvailableCalendars.Any(item => item.IsSelected) && AvailableCalendars.Count > 0)
        {
            AvailableCalendars[0].IsSelected = true;
        }

        RefreshCalendarNames();
        SettingsPersistenceRequest settingsSnapshot;
        AppSettings previousSettings;
        var previousEditorCalendarId = EditorCalendarId;
        string activeCalendarId;
        lock (_settingsStateLock)
        {
            previousSettings = CreateSettingsPersistenceSnapshotUnsafe();
            _settings.VisibleCalendarIds = AvailableCalendars.Where(item => item.IsSelected).Select(item => item.Id).ToList();
            _settings.ActiveCalendarId = _settings.VisibleCalendarIds.FirstOrDefault() ?? ResolveEditorCalendarId();
            activeCalendarId = _settings.ActiveCalendarId;
            settingsSnapshot = CreateSettingsPersistenceRequestUnsafe();
        }
        if (!settingsSnapshot.Settings.VisibleCalendarIds.Contains(EditorCalendarId, StringComparer.Ordinal))
        {
            EditorCalendarId = activeCalendarId;
        }

        try
        {
            await PersistSettingsAsync(settingsSnapshot);
        }
        catch (Exception ex)
        {
            var restored = false;
            lock (_settingsStateLock)
            {
                if (_settingsRevision == settingsSnapshot.Revision)
                {
                    _settings = previousSettings;
                    restored = true;
                }
            }

            if (restored)
            {
                RestoreCalendarSelection(previousSettings, previousEditorCalendarId);
            }

            Status = $"表示カレンダー設定を保存できませんでした: {ex.Message}";
            throw new InvalidOperationException(Status, ex);
        }

        try
        {
            await RefreshCalendarAsync();
            if (SelectedEvent is not null
                && AvailableCalendars.Count > 0
                && !AvailableCalendars.Any(item => item.IsSelected
                    && string.Equals(item.Id, SelectedEvent.CalendarId, StringComparison.Ordinal)))
            {
                SelectedEvent = null;
            }
        }
        catch (Exception ex)
        {
            Status = $"表示カレンダーは保存しましたが、カレンダー再読込に失敗しました: {ex.Message}";
            throw new InvalidOperationException(Status, ex);
        }

        try
        {
            await RefreshOperationalStatusAsync(null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            Status = $"表示カレンダーを更新しました。状態表示の更新に失敗しました: {ex.Message}";
        }
    }

    private void RestoreCalendarSelection(AppSettings previousSettings, string previousEditorCalendarId)
    {
        IReadOnlyCollection<string> selectedIds = previousSettings.VisibleCalendarIds.Count == 0
            ? new[] { string.IsNullOrWhiteSpace(previousSettings.ActiveCalendarId)
                ? GoogleCalendarDefaults.PrimaryCalendarId
                : previousSettings.ActiveCalendarId }
            : previousSettings.VisibleCalendarIds;

        foreach (var calendar in AvailableCalendars)
        {
            calendar.IsSelected = selectedIds.Contains(calendar.Id, StringComparer.Ordinal);
        }

        if (!AvailableCalendars.Any(item => item.IsSelected) && AvailableCalendars.Count > 0)
        {
            var fallback = AvailableCalendars.FirstOrDefault(item => item.Id == previousSettings.ActiveCalendarId)
                ?? AvailableCalendars[0];
            fallback.IsSelected = true;
        }

        RefreshCalendarNames();
        EditorCalendarId = AvailableCalendars.Any(item => item.IsSelected && item.Id == previousEditorCalendarId)
            ? previousEditorCalendarId
            : ResolveEditorCalendarId();
    }

    private void ApplyDisplayColors(IEnumerable<CalendarEvent> events)
    {
        ApplyDisplayColors(events, CreateCalendarSnapshotBuildContext());
    }

    private void ApplyDisplayColors(IEnumerable<CalendarEvent> events, CalendarSnapshotBuildContext context)
    {
        foreach (var calendarEvent in events)
        {
            var colors = TagService.ResolveDisplayColors(calendarEvent, context.EventColorPalette);
            calendarEvent.DisplayColor = colors.Background;
            calendarEvent.DisplayForegroundColor = colors.Foreground;
            calendarEvent.ToolTipText = CalendarEventToolTipFormatter.Format(
                calendarEvent,
                context.CalendarNames.GetValueOrDefault(calendarEvent.CalendarId));
        }
    }

    private bool IsVisible(CalendarEvent calendarEvent)
    {
        return IsInVisibleCalendar(calendarEvent)
            && !TagService.IsDayCellDirective(calendarEvent);
    }

    private static bool IsVisible(CalendarEvent calendarEvent, CalendarSnapshotBuildContext context)
    {
        return IsInVisibleCalendar(calendarEvent, context)
            && !TagService.IsDayCellDirective(calendarEvent);
    }

    private bool IsInVisibleCalendar(CalendarEvent calendarEvent) =>
        AvailableCalendars.Count == 0
        || AvailableCalendars.Any(item => item.IsSelected && item.Id == calendarEvent.CalendarId);

    private static bool IsInVisibleCalendar(CalendarEvent calendarEvent, CalendarSnapshotBuildContext context) =>
        context.VisibleCalendarIds.Count == 0
        || context.VisibleCalendarIds.Contains(calendarEvent.CalendarId);

    private CalendarSnapshotBuildContext CreateCalendarSnapshotBuildContext()
    {
        return new CalendarSnapshotBuildContext(
            _settings.WeekStartsOnMonday,
            AvailableCalendars
                .Where(item => item.IsSelected)
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal),
            AvailableCalendars.ToDictionary(item => item.Id, item => item.Summary, StringComparer.Ordinal),
            _eventColorPalette.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    private async Task<IReadOnlyList<GoogleCalendarSelectionItem>> LoadAvailableCalendarsCoreAsync()
    {
        var settings = CreateSettingsSnapshot();
        IReadOnlyList<GoogleCalendarInfo> calendars;
        if (!string.IsNullOrWhiteSpace(settings.OAuthClientJsonPath) && File.Exists(settings.OAuthClientJsonPath))
        {
            try
            {
                calendars = await _syncService.ListCalendarsAsync(settings.OAuthClientJsonPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Status = "Googleカレンダー一覧を取得できませんでした。OAuth設定またはネットワークを確認してください。";
                calendars = [];
            }
        }
        else
        {
            calendars = [];
        }

        var selectedIds = settings.VisibleCalendarIds.Count == 0
            ? [string.IsNullOrWhiteSpace(settings.ActiveCalendarId) ? GoogleCalendarDefaults.PrimaryCalendarId : settings.ActiveCalendarId]
            : settings.VisibleCalendarIds;

        if (calendars.Count == 0)
        {
            calendars = selectedIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Select(id => new GoogleCalendarInfo(id, id))
                .ToList();
        }

        if (calendars.Count == 0)
        {
            calendars = [new GoogleCalendarInfo(GoogleCalendarDefaults.PrimaryCalendarId, "primary")];
        }

        return calendars
            .Select(calendar => new GoogleCalendarSelectionItem
            {
                Id = calendar.Id,
                Summary = calendar.Summary,
                IsSelected = selectedIds.Contains(calendar.Id, StringComparer.Ordinal)
            })
            .ToArray();
    }

    private void RefreshCalendarNames()
    {
        CalendarNames.Clear();
        foreach (var calendar in AvailableCalendars)
        {
            CalendarNames.Add(calendar.Summary);
        }
    }
}
