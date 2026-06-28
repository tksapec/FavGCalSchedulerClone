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

    public async Task RefreshOperationalStatusAsync(ReminderMonitoringSnapshot? reminderDiagnostics)
    {
        var diagnostics = await _syncService.LoadDiagnosticsAsync(_settings);
        SyncStatusText = diagnostics.LastResult is null
            ? $"同期: 未同期 {diagnostics.DirtyCount} 件 / 最終同期なし"
            : $"同期: 未同期 {diagnostics.DirtyCount} 件 / 最終 {diagnostics.LastResult.FinishedAt:MM/dd HH:mm}";
        if (reminderDiagnostics is not null)
        {
            ReminderStatusText = FormatReminderStatus(reminderDiagnostics);
        }

        LastErrorStatusText = BuildLastErrorStatus(diagnostics, reminderDiagnostics);
    }

    public void UpdateReminderOperationalStatus(ReminderMonitoringSnapshot reminderDiagnostics)
    {
        ReminderStatusText = FormatReminderStatus(reminderDiagnostics);
        LastErrorStatusText = BuildLastErrorStatus(null, reminderDiagnostics);
    }

    public async Task SelectReminderEventAsync(string eventId, DateTimeOffset occurrenceStart)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return;
        }

        await NavigateToDateAsync(occurrenceStart.Date);
        SelectedEvent = _visibleEvents.FirstOrDefault(item =>
                string.Equals(item.Id, eventId, StringComparison.Ordinal)
                && item.Start.Date == occurrenceStart.Date)
            ?? _visibleEvents.FirstOrDefault(item => string.Equals(item.Id, eventId, StringComparison.Ordinal));
    }

    public async Task<CalendarEvent> CreateTwoMinuteReminderTestEventAsync()
    {
        var start = DateTimeOffset.Now.AddMinutes(2);
        var testEvent = new CalendarEvent
        {
            CalendarId = ResolveEditorCalendarId(),
            Title = "通知確認テスト",
            Description = "2分後の通知確認用に作成されました。",
            Start = start,
            End = start.AddMinutes(30),
            IsAllDay = false,
            ReminderMinutesBeforeStart = 0,
            IsDirty = true
        };
        await _repository.SaveEventAsync(testEvent);
        await RefreshCalendarAsync();
        SelectedEvent = _visibleEvents.FirstOrDefault(item => item.Id == testEvent.Id) ?? testEvent;
        Status = "2分後の通知確認テスト予定を作成しました。";
        await RefreshOperationalStatusAsync(null);
        return testEvent;
    }

    private static string FormatReminderStatus(ReminderMonitoringSnapshot value) =>
        $"通知監視: {(value.IsRunning ? "起動中" : "停止中")} / 次回 {FormatStatusDate(value.NextCheckAt)}";

    private static string FormatStatusDate(DateTimeOffset? value) => value?.ToString("MM/dd HH:mm") ?? "未定";

    private static string BuildLastErrorStatus(SyncDiagnosticsSnapshot? sync, ReminderMonitoringSnapshot? reminder)
    {
        if (reminder?.LastError is { Length: > 0 } reminderError)
        {
            return $"通知エラー: {TrimStatusError(reminderError)}";
        }

        if (sync?.LastResult is { Failed: > 0 } result)
        {
            return $"同期エラー: {result.Failed} 件";
        }

        return "";
    }

    private static string TrimStatusError(string value)
    {
        var normalized = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 80 ? normalized : normalized[..80] + "...";
    }
}
