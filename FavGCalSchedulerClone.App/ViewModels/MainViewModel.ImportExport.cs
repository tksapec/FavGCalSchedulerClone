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

    public async Task<CalendarCsvExportResult> ExportCurrentYearCsvAsync(string csvPath)
    {
        var events = await LoadYearEventsAsync(CurrentMonth);
        var result = await _csvService.ExportAsync(events, csvPath);
        Status = $"CSVへエクスポートしました: {result.ExportedCount} 件";
        return result;
    }

    public async Task<CalendarCsvImportResult> ImportCsvAsync(string csvPath)
    {
        var result = await _csvService.ImportAsync(csvPath);
        foreach (var calendarEvent in result.Events)
        {
            await _repository.SaveEventAsync(calendarEvent);
        }

        await RefreshCalendarAsync();
        Status = result.Errors.Count == 0
            ? $"CSVからインポートしました: {result.Events.Count} 件"
            : $"CSVから {result.Events.Count} 件をインポートしました。エラー {result.Errors.Count} 件。";
        return result;
    }

    public Task<FavGCalImportAnalysis> AnalyzeFavGCalSchedulerImportAsync(string sourceFolder)
    {
        return _favGCalImportService.AnalyzeAsync(sourceFolder);
    }

    public async Task<FavGCalImportResult> ImportFavGCalSchedulerAsync(FavGCalImportOptions options)
    {
        var mappedCalendarIds = options.CalendarMappings.Values
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var calendarId in mappedCalendarIds)
        {
            if (!AvailableCalendars.Any(item => item.Id == calendarId))
            {
                AvailableCalendars.Add(new GoogleCalendarSelectionItem
                {
                    Id = calendarId,
                    Summary = calendarId,
                    IsSelected = true
                });
            }
        }

        foreach (var calendar in AvailableCalendars)
        {
            if (mappedCalendarIds.Contains(calendar.Id, StringComparer.Ordinal))
            {
                calendar.IsSelected = true;
            }
        }

        if (options.ImportSettings)
        {
            ApplyFavGCalSchedulerSettings(options.SourceFolder);
        }

        await SaveOAuthPathAsync();
        if (options.VerifyGoogleEventsBeforeImport
            && mappedCalendarIds.Length > 0
            && !string.IsNullOrWhiteSpace(_settings.OAuthClientJsonPath)
            && File.Exists(_settings.OAuthClientJsonPath))
        {
            Status = "Googleカレンダーから既存予定を確認しています...";
            await _syncService.PullAsync(_settings, mappedCalendarIds);
        }

        var result = await _favGCalImportService.ImportAsync(options);
        if (options.ImportSettings)
        {
            await SaveApplicationSettingsAsync(_settings);
        }

        await ReloadAvailableCalendarsAsync();
        await RefreshCalendarAsync();
        Status = $"FavGCalSchedulerデータを取り込みました: 追加 {result.ImportedCount} 件、既存紐付け {result.LinkedExistingGoogleCount} 件、重複スキップ {result.SkippedDuplicateCount} 件、ToDo内容修復 {result.CorrectedTodoDescriptionCount} 件";
        return result;
    }

    private void ApplyFavGCalSchedulerSettings(string sourceFolder)
    {
        var iniPath = Path.Combine(sourceFolder, "FavGCalScheduler.ini");
        if (!File.Exists(iniPath))
        {
            return;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;
        foreach (var line in File.ReadLines(iniPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1].Trim();
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();
            if (section.Equals("DISP_INFO", StringComparison.OrdinalIgnoreCase)
                && new[] { "DeletePopup", "AppClose", "EditScheduleWindowHide", "StartWeekdayIndex", "WeekdayType", "FontSize", "BottomInfoFontSize", "ToDoRunLimitMonthCount", "ToDoCompLimitMonthCount" }
                    .Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                values[key] = value;
            }
            else if (section.Equals("APP_INFO", StringComparison.OrdinalIgnoreCase)
                     && new[] { "CreateScheduleNoHistory", "ScheduleDeaultAllDay", "ScheduleDeaultAlarmIndex" }
                         .Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                values[key] = value;
            }
            else if (section.Equals("SYNC_INFO", StringComparison.OrdinalIgnoreCase)
                     && new[] { "AddEditDelSync", "SyncIntervalMin" }.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                values[key] = value;
            }
        }

        if (values.TryGetValue("DeletePopup", out var deletePopup))
        {
            _settings.ConfirmBeforeDelete = deletePopup != "0";
        }

        if (values.TryGetValue("AppClose", out var appClose))
        {
            _settings.CloseButtonExitsApplication = appClose != "0";
        }

        if (values.TryGetValue("ScheduleDeaultAllDay", out var defaultAllDay))
        {
            _settings.DefaultNewEventIsAllDay = defaultAllDay != "0";
        }

        if (values.TryGetValue("EditScheduleWindowHide", out var editScheduleWindowHide))
        {
            _settings.HideMainWindowWhileEditingSchedule = editScheduleWindowHide != "0";
        }

        if (values.TryGetValue("StartWeekdayIndex", out var startWeekday)
            && int.TryParse(startWeekday, out var startWeekdayIndex))
        {
            _settings.WeekStartsOnMonday = startWeekdayIndex == 1;
        }

        if (values.TryGetValue("WeekdayType", out var weekdayType)
            && int.TryParse(weekdayType, out var weekdayTypeIndex))
        {
            _settings.WeekdayDisplayType = weekdayTypeIndex switch
            {
                1 => WeekdayDisplayType.EnglishFull,
                2 => WeekdayDisplayType.JapaneseShort,
                _ => WeekdayDisplayType.EnglishShort
            };
        }

        if (values.TryGetValue("FontSize", out var fontSize) && int.TryParse(fontSize, out var fontIndex))
        {
            _settings.CalendarLabelFontSizeIndex = Math.Clamp(fontIndex + 1, 1, 3);
        }

        if (values.TryGetValue("BottomInfoFontSize", out var sideFontSize) && int.TryParse(sideFontSize, out var sideFontIndex))
        {
            _settings.SideListFontSizeIndex = Math.Clamp(sideFontIndex + 1, 1, 3);
        }

        if (values.TryGetValue("ToDoRunLimitMonthCount", out var runningLimit) && int.TryParse(runningLimit, out var runningMonths))
        {
            _settings.IncompleteTodoDisplayPeriodMonths = AppSettingsNormalizer.NormalizeTodoMonths(runningMonths);
        }

        if (values.TryGetValue("ToDoCompLimitMonthCount", out var completedLimit) && int.TryParse(completedLimit, out var completedMonths))
        {
            _settings.CompletedTodoDisplayPeriodMonths = AppSettingsNormalizer.NormalizeTodoMonths(completedMonths);
        }

        if (values.TryGetValue("CreateScheduleNoHistory", out var noHistory))
        {
            _settings.ReuseLastScheduleInput = noHistory == "0";
        }

        if (values.TryGetValue("ScheduleDeaultAlarmIndex", out var alarmIndex) && int.TryParse(alarmIndex, out var alarm))
        {
            _settings.DefaultScheduleReminderMinutes = alarm switch
            {
                1 => 0,
                2 => 5,
                3 => 10,
                4 => 30,
                5 => 60,
                _ => null
            };
        }

        if (values.TryGetValue("AddEditDelSync", out var syncAfterLocalChange))
        {
            _settings.SyncAfterLocalChange = syncAfterLocalChange != "0";
        }

        if (values.TryGetValue("SyncIntervalMin", out var syncMinutes) && int.TryParse(syncMinutes, out var interval))
        {
            _settings.AutomaticSyncIntervalMinutes = new[] { 30, 60, 120, 360 }.Contains(interval) ? interval : null;
        }
    }
}
