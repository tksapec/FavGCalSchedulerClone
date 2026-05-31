using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class DialogDtoTests
{
    [Fact]
    public void ScheduleEditorResult_PreservesEditorValues()
    {
        var result = new ScheduleEditorResult(
            "primary",
            "7",
            new DateTime(2026, 5, 22),
            new DateTime(2026, 5, 23),
            "10:00",
            "12:30",
            true,
            10,
            "C202a",
            "水田C 富士電機来社対応",
            "2026/05/12 14:12\r\nAN-40C 予備検証試験");

        Assert.Equal("primary", result.CalendarId);
        Assert.Equal("7", result.ColorId);
        Assert.Equal(new DateTime(2026, 5, 22), result.StartDate);
        Assert.Equal(new DateTime(2026, 5, 23), result.EndDate);
        Assert.Equal("10:00", result.StartTime);
        Assert.Equal("12:30", result.EndTime);
        Assert.True(result.IsAllDay);
        Assert.Equal(10, result.ReminderMinutesBeforeStart);
        Assert.Equal("C202a", result.Location);
        Assert.Equal("水田C 富士電機来社対応", result.Title);
        Assert.Equal("2026/05/12 14:12\r\nAN-40C 予備検証試験", result.Description);
    }

    [Fact]
    public void TodoEditorResult_PreservesMetadataAndBodyNewlines()
    {
        var body = "2026/05/20 11:40\r\n\r\n廣田s：2026/05/20 12:31 江島s";
        var result = new TodoEditorResult(
            "primary",
            "5",
            new DateTime(2026, 5, 20),
            "A",
            60,
            "国際営業部 内田s 贈り物賛同者連絡",
            body);

        Assert.Equal("primary", result.CalendarId);
        Assert.Equal("5", result.ColorId);
        Assert.Equal(new DateTime(2026, 5, 20), result.DueDate);
        Assert.Equal("A", result.Priority);
        Assert.Equal(60, result.Progress);
        Assert.Equal("国際営業部 内田s 贈り物賛同者連絡", result.Title);
        Assert.Equal(body, result.Description);
    }

    [Fact]
    public void SettingsDialogResult_PreservesSettingsAndOAuthPath()
    {
        var settings = new AppSettings
        {
            StartupCalendarViewMode = CalendarViewMode.Week,
            StartupTodoTabIndex = 1,
            WeekStartsOnMonday = true,
            ShowSyncPreviewBeforeManualSync = true,
            EnableSyncDiagnostics = true,
            SyncConflictPolicy = SyncConflictPolicy.PreferLocal
        };

        var result = new SettingsDialogResult(settings, @"C:\temp\client_secret.json");

        Assert.Same(settings, result.Settings);
        Assert.Equal(CalendarViewMode.Week, result.Settings.StartupCalendarViewMode);
        Assert.Equal(1, result.Settings.StartupTodoTabIndex);
        Assert.True(result.Settings.WeekStartsOnMonday);
        Assert.True(result.Settings.ShowSyncPreviewBeforeManualSync);
        Assert.True(result.Settings.EnableSyncDiagnostics);
        Assert.Equal(SyncConflictPolicy.PreferLocal, result.Settings.SyncConflictPolicy);
        Assert.Equal(@"C:\temp\client_secret.json", result.OAuthClientJsonPath);
    }
}
