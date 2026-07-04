using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using System.Text.Json;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarRepositoryTests
{
    [Fact]
    public void AppSettings_DeserializesLegacyJsonWithDefaults()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """
            {
              "OAuthClientJsonPath": "client.json",
              "ActiveCalendarId": "primary",
              "DisplayMonth": "2026-05-01T00:00:00"
            }
            """);

        Assert.NotNull(settings);
        Assert.Equal("client.json", settings.OAuthClientJsonPath);
        Assert.Empty(settings.VisibleCalendarIds);
        Assert.Equal(0, settings.StartupTabIndex);
        Assert.True(settings.ConfirmBeforeDelete);
        Assert.True(settings.CloseButtonExitsApplication);
        Assert.True(settings.DefaultNewEventIsAllDay);
        Assert.True(settings.UseWindowsToastNotifications);
        Assert.Equal(CalendarViewMode.Month, settings.StartupCalendarViewMode);
        Assert.Equal(2, settings.CalendarLabelFontSizeIndex);
        Assert.Equal(2, settings.SideListFontSizeIndex);
        Assert.Equal(255, settings.WindowOpacity);
        Assert.False(settings.SyncAfterLocalChange);
        Assert.Null(settings.LastManualSyncAt);
        Assert.Null(settings.LastAutomaticSyncAt);
        Assert.False(settings.ShowSyncPreviewBeforeManualSync);
        Assert.False(settings.EnableSyncDiagnostics);
        Assert.Equal(SyncConflictPolicy.SkipLocalDirty, settings.SyncConflictPolicy);
    }

    [Fact]
    public async Task SaveSettingsAsync_RoundTripsApplicationSettings()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();

        await repository.SaveSettingsAsync(new AppSettings
        {
            OAuthClientJsonPath = "client.json",
            ActiveCalendarId = "primary",
            VisibleCalendarIds = ["primary", "team"],
            DisplayMonth = new DateTime(2026, 5, 1),
            StartupTabIndex = 3,
            ConfirmBeforeDelete = false,
            CloseButtonExitsApplication = false,
            DefaultNewEventIsAllDay = false,
            UseWindowsToastNotifications = false,
            StartupCalendarViewMode = CalendarViewMode.Week,
            StartupTodoTabIndex = 1,
            HideMainWindowWhileEditingSchedule = true,
            ReuseLastScheduleInput = true,
            DefaultScheduleReminderMinutes = 10,
            CalendarLabelFontSizeIndex = 3,
            SideListFontSizeIndex = 1,
            WeekdayDisplayType = WeekdayDisplayType.JapaneseShort,
            WeekStartsOnMonday = true,
            WindowOpacity = 180,
            IncompleteTodoDisplayPeriodMonths = 3,
            CompletedTodoDisplayPeriodMonths = 12,
            EnableReminderSound = true,
            ReminderSoundFilePath = "sound.wav",
            ReminderSoundVolume = 40,
            SyncAfterLocalChange = true,
            AutomaticSyncIntervalMinutes = 120,
            LastManualSyncAt = new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.FromHours(9)),
            LastAutomaticSyncAt = new DateTimeOffset(2026, 5, 18, 13, 0, 0, TimeSpan.FromHours(9)),
            ShowSyncPreviewBeforeManualSync = true,
            EnableSyncDiagnostics = true,
            SyncConflictPolicy = SyncConflictPolicy.PreferGoogle
        });

        var settings = await repository.LoadSettingsAsync();

        Assert.Equal("client.json", settings.OAuthClientJsonPath);
        Assert.Equal(["primary", "team"], settings.VisibleCalendarIds);
        Assert.Equal(new DateTime(2026, 5, 1), settings.DisplayMonth);
        Assert.Equal(3, settings.StartupTabIndex);
        Assert.False(settings.ConfirmBeforeDelete);
        Assert.False(settings.CloseButtonExitsApplication);
        Assert.False(settings.DefaultNewEventIsAllDay);
        Assert.False(settings.UseWindowsToastNotifications);
        Assert.Equal(CalendarViewMode.Week, settings.StartupCalendarViewMode);
        Assert.Equal(1, settings.StartupTodoTabIndex);
        Assert.True(settings.HideMainWindowWhileEditingSchedule);
        Assert.True(settings.ReuseLastScheduleInput);
        Assert.Equal(10, settings.DefaultScheduleReminderMinutes);
        Assert.Equal(3, settings.CalendarLabelFontSizeIndex);
        Assert.Equal(1, settings.SideListFontSizeIndex);
        Assert.Equal(WeekdayDisplayType.JapaneseShort, settings.WeekdayDisplayType);
        Assert.True(settings.WeekStartsOnMonday);
        Assert.Equal(180, settings.WindowOpacity);
        Assert.Equal(3, settings.IncompleteTodoDisplayPeriodMonths);
        Assert.Equal(12, settings.CompletedTodoDisplayPeriodMonths);
        Assert.True(settings.EnableReminderSound);
        Assert.Equal("sound.wav", settings.ReminderSoundFilePath);
        Assert.Equal(40, settings.ReminderSoundVolume);
        Assert.True(settings.SyncAfterLocalChange);
        Assert.Equal(120, settings.AutomaticSyncIntervalMinutes);
        Assert.Equal(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.FromHours(9)), settings.LastManualSyncAt);
        Assert.Equal(new DateTimeOffset(2026, 5, 18, 13, 0, 0, TimeSpan.FromHours(9)), settings.LastAutomaticSyncAt);
        Assert.True(settings.ShowSyncPreviewBeforeManualSync);
        Assert.True(settings.EnableSyncDiagnostics);
        Assert.Equal(SyncConflictPolicy.PreferGoogle, settings.SyncConflictPolicy);
    }

    [Fact]
    public async Task UpsertSyncedEventAsync_MergesByGoogleEventId()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();

        var local = new CalendarEvent
        {
            Title = "local",
            CalendarId = "primary",
            GoogleEventId = "google-1",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero),
            IsDirty = false
        };
        await repository.SaveEventAsync(local);

        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "g:primary:google-1",
            Title = "remote",
            CalendarId = "primary",
            GoogleEventId = "google-1",
            Start = local.Start,
            End = local.End
        });

        var events = await repository.LoadEventsAsync(local.Start.AddHours(-1), local.End.AddHours(1));

        Assert.Single(events);
        Assert.Equal(local.Id, events[0].Id);
        Assert.Equal("remote", events[0].Title);
    }

    [Fact]
    public async Task SaveEventAsync_PreservesExistingGoogleEventIdWhenEditedCopyIsStale()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var local = new CalendarEvent
        {
            Id = "local-1",
            Title = "synced",
            CalendarId = "primary",
            GoogleEventId = "google-1",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero),
            IsDirty = false,
            LastSyncedAt = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(local);

        await repository.SaveEventAsync(new CalendarEvent
        {
            Id = "local-1",
            Title = "edited",
            CalendarId = "primary",
            Start = local.Start,
            End = local.End,
            IsDirty = true
        });

        var stored = await repository.FindMasterByIdAsync("local-1");
        Assert.NotNull(stored);
        Assert.Equal("google-1", stored!.GoogleEventId);
        Assert.Equal(local.LastSyncedAt, stored.LastSyncedAt);
        Assert.True(stored.IsDirty);
    }

    [Fact]
    public async Task SaveEventAsync_RoundTripsRecurrenceFields()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();

        var item = new CalendarEvent
        {
            Id = "exception-1",
            Title = "Occurrence override",
            CalendarId = "primary",
            GoogleEventId = "instance-1",
            RecurringEventId = "series-1",
            RecurringParentId = "local-series-1",
            OriginalStart = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero),
            IsRecurrenceException = true,
            Start = new DateTimeOffset(2026, 5, 16, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero),
            ReminderMinutesBeforeStart = 10
        };

        await repository.SaveEventAsync(item);
        var loaded = await repository.FindEventByGoogleEventIdAsync("primary", "instance-1");

        Assert.NotNull(loaded);
        Assert.Equal("series-1", loaded!.RecurringEventId);
        Assert.Equal("local-series-1", loaded.RecurringParentId);
        Assert.Equal(item.OriginalStart, loaded.OriginalStart);
        Assert.True(loaded.IsRecurrenceException);
        Assert.Equal(10, loaded.ReminderMinutesBeforeStart);
    }

    [Fact]
    public async Task SaveEventAsync_RoundTripsDateTimeOffsetOffsets()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var item = new CalendarEvent
        {
            Id = "offset-event",
            Title = "Offset event",
            CalendarId = "primary",
            Start = new DateTimeOffset(2026, 7, 4, 9, 30, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 7, 4, 10, 30, 0, TimeSpan.FromHours(9)),
            LastSyncedAt = new DateTimeOffset(2026, 7, 4, 11, 30, 0, TimeSpan.FromHours(9))
        };

        await repository.SaveEventAsync(item);
        var loaded = await repository.FindEventByIdAsync("offset-event");

        Assert.NotNull(loaded);
        Assert.Equal(item.Start, loaded!.Start);
        Assert.Equal(item.Start.Offset, loaded.Start.Offset);
        Assert.Equal(item.End, loaded.End);
        Assert.Equal(item.LastSyncedAt, loaded.LastSyncedAt);
    }

    [Fact]
    public async Task InitializeAsync_AllowsFilenameOnlyDatabasePath()
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            Directory.SetCurrentDirectory(tempDirectory);
            var repository = new CalendarRepository("calendar.db");

            await repository.InitializeAsync();

            Assert.True(File.Exists(Path.Combine(tempDirectory, "calendar.db")));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveEventAsync_RoundTripsGoogleReminderMetadata()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var item = new CalendarEvent
        {
            Id = "google-reminder",
            Title = "Google reminder",
            CalendarId = "primary",
            Start = new DateTimeOffset(2026, 5, 16, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero),
            GoogleReminderMetadata = new GoogleReminderMetadata
            {
                UseDefault = false,
                PopupMinutes = [10],
                EmailMinutes = [30],
                AdoptedReminderMinutes = 10,
                Source = "explicit"
            }
        };

        await repository.SaveEventAsync(item);
        var loaded = await repository.FindEventByIdAsync(item.Id);

        Assert.NotNull(loaded?.GoogleReminderMetadata);
        Assert.Equal([10], loaded!.GoogleReminderMetadata!.PopupMinutes);
        Assert.Equal([30], loaded.GoogleReminderMetadata.EmailMinutes);
        Assert.Equal(10, loaded.GoogleReminderMetadata.AdoptedReminderMinutes);
    }

    [Fact]
    public async Task SaveEventAsync_RoundTripsSeparateReminderEnabledFlags()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var item = new CalendarEvent
        {
            Id = "separate-reminders",
            Title = "Separate reminders",
            CalendarId = "primary",
            Start = new DateTimeOffset(2026, 5, 16, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero),
            ReminderMinutesBeforeStart = 30,
            IsAppReminderEnabled = false,
            IsGoogleEmailReminderEnabled = true
        };

        await repository.SaveEventAsync(item);
        var loaded = await repository.FindEventByIdAsync(item.Id);

        Assert.NotNull(loaded);
        Assert.Equal(30, loaded!.ReminderMinutesBeforeStart);
        Assert.False(loaded.IsAppReminderEnabled);
        Assert.True(loaded.IsGoogleEmailReminderEnabled);
    }

    [Fact]
    public async Task LoadEventsAsync_InfersReminderEnabledFlagsForLegacyRows()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO events(
                    id, calendar_id, title, start, end, is_all_day,
                    reminder_minutes_before_start, is_deleted, updated_at, is_dirty, is_todo_like,
                    google_reminder_metadata_json)
                VALUES(
                    'legacy-reminders', 'primary', 'Legacy reminders', '2026-05-16T11:00:00.0000000+00:00', '2026-05-16T12:00:00.0000000+00:00', 0,
                    30, 0, '2026-05-16T10:00:00.0000000+00:00', 0, 0,
                    '{"EmailMinutes":[30]}')
                """;
            await command.ExecuteNonQueryAsync();
        }

        var loaded = await repository.FindEventByIdAsync("legacy-reminders");

        Assert.NotNull(loaded);
        Assert.Equal(30, loaded!.ReminderMinutesBeforeStart);
        Assert.True(loaded.IsAppReminderEnabled);
        Assert.True(loaded.IsGoogleEmailReminderEnabled);
    }

    [Fact]
    public async Task InitializeAsync_EnablesWalJournalMode()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);

        await repository.InitializeAsync();

        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = dbPath
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        var mode = await command.ExecuteScalarAsync() as string;

        Assert.Equal("wal", mode, ignoreCase: true);
    }

    [Fact]
    public async Task MarkSyncedByIdsAsync_UpdatesExistingDistinctIdsAndPreservesReminderDetails()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var item1 = DirtyEvent("sync-1", "Sync 1");
        item1.ReminderMinutesBeforeStart = 30;
        item1.IsAppReminderEnabled = false;
        item1.IsGoogleEmailReminderEnabled = true;
        item1.GoogleReminderMetadata = new GoogleReminderMetadata
        {
            UseDefault = false,
            EmailMinutes = [30],
            AdoptedReminderMinutes = 30,
            AdoptedReminderMethod = "email",
            Source = "explicit"
        };
        var item2 = DirtyEvent("sync-2", "Sync 2");
        var item3 = DirtyEvent("sync-3", "Sync 3");
        await repository.SaveEventAsync(item1);
        await repository.SaveEventAsync(item2);
        await repository.SaveEventAsync(item3);
        var stored3BeforeSync = await repository.FindEventByIdAsync("sync-3");

        var updated = await repository.MarkSyncedByIdsAsync(["sync-1", "", "missing", "sync-2", "sync-1", "   "]);

        Assert.Equal(2, updated);
        var stored1 = await repository.FindEventByIdAsync("sync-1");
        var stored2 = await repository.FindEventByIdAsync("sync-2");
        var stored3 = await repository.FindEventByIdAsync("sync-3");
        Assert.NotNull(stored1);
        Assert.NotNull(stored2);
        Assert.NotNull(stored3);
        Assert.False(stored1!.IsDirty);
        Assert.Null(stored1.DirtyFields);
        Assert.NotNull(stored1.LastSyncedAt);
        Assert.Equal(30, stored1.ReminderMinutesBeforeStart);
        Assert.False(stored1.IsAppReminderEnabled);
        Assert.True(stored1.IsGoogleEmailReminderEnabled);
        Assert.Equal([30], stored1.GoogleReminderMetadata?.EmailMinutes);
        Assert.False(stored2!.IsDirty);
        Assert.Null(stored2.DirtyFields);
        Assert.NotNull(stored2.LastSyncedAt);
        Assert.True(stored3!.IsDirty);
        Assert.Equal(stored3BeforeSync?.DirtyFields, stored3.DirtyFields);
        Assert.Null(stored3.LastSyncedAt);
    }

    private static CalendarEvent DirtyEvent(string id, string title) => new()
    {
        Id = id,
        Title = title,
        CalendarId = "primary",
        Start = new DateTimeOffset(2026, 5, 16, 11, 0, 0, TimeSpan.Zero),
        End = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero),
        IsDirty = true,
        DirtyFields = "Title"
    };
}
