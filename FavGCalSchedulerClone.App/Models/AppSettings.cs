namespace FavGCalSchedulerClone.App.Models;

public sealed class AppSettings
{
    public string? OAuthClientJsonPath { get; set; }
    public string ActiveCalendarId { get; set; } = GoogleCalendarDefaults.PrimaryCalendarId;
    public List<string> VisibleCalendarIds { get; set; } = [];
    public DateTime DisplayMonth { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public int StartupTabIndex { get; set; }
    public CalendarViewMode StartupCalendarViewMode { get; set; } = CalendarViewMode.Month;
    public int StartupTodoTabIndex { get; set; }
    public bool ConfirmBeforeDelete { get; set; } = true;
    public bool CloseButtonExitsApplication { get; set; } = true;
    public bool DefaultNewEventIsAllDay { get; set; } = true;
    public bool HideMainWindowWhileEditingSchedule { get; set; }
    public bool ReuseLastScheduleInput { get; set; }
    public int? DefaultScheduleReminderMinutes { get; set; }
    public int CalendarLabelFontSizeIndex { get; set; } = 2;
    public int SideListFontSizeIndex { get; set; } = 2;
    public WeekdayDisplayType WeekdayDisplayType { get; set; } = WeekdayDisplayType.EnglishShort;
    public bool WeekStartsOnMonday { get; set; }
    public int WindowOpacity { get; set; } = 255;
    public int IncompleteTodoDisplayPeriodMonths { get; set; }
    public int CompletedTodoDisplayPeriodMonths { get; set; }
    public bool EnableReminderSound { get; set; }
    public string? ReminderSoundFilePath { get; set; }
    public int ReminderSoundVolume { get; set; } = 50;
    public bool UseWindowsToastNotifications { get; set; } = true;
    public bool SyncAfterLocalChange { get; set; }
    public int? AutomaticSyncIntervalMinutes { get; set; }
    public DateTimeOffset? LastManualSyncAt { get; set; }
    public DateTimeOffset? LastAutomaticSyncAt { get; set; }
    public bool ShowSyncPreviewBeforeManualSync { get; set; }
    public bool EnableSyncDiagnostics { get; set; }
    public SyncConflictPolicy SyncConflictPolicy { get; set; } = SyncConflictPolicy.SkipLocalDirty;
}

public enum WeekdayDisplayType
{
    EnglishFull,
    EnglishShort,
    JapaneseShort
}

public enum SyncConflictPolicy
{
    SkipLocalDirty,
    PreferLocal,
    PreferGoogle
}
