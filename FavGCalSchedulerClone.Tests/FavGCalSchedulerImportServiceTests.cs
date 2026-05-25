using System.IO.Compression;
using System.Text;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class FavGCalSchedulerImportServiceTests
{
    [Fact]
    public void ExtractCalendarIdFromFeedUrl_DecodesLegacyGoogleFeedUrl()
    {
        var id = FavGCalSchedulerImportService.ExtractCalendarIdFromFeedUrl(
            "https://www.google.com/calendar/feeds/user%40example.com/private/full");

        Assert.Equal("user@example.com", id);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "1")]
    [InlineData(2, "2")]
    [InlineData(11, "11")]
    [InlineData(12, null)]
    [InlineData(-1, null)]
    public void MapLegacyColorToGoogleColorId_UsesLegacyLabelPalette(int rawColorIndex, string? expected)
    {
        Assert.Equal(expected, FavGCalSchedulerImportService.MapLegacyColorToGoogleColorId(rawColorIndex));
    }

    [Fact]
    public async Task AnalyzeAsync_ReadsScheduleIniAndFavCalEvents()
    {
        var sourceFolder = CreateLegacyFolder();
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var service = new FavGCalSchedulerImportService(repository);

        var analysis = await service.AnalyzeAsync(sourceFolder);

        Assert.Single(analysis.Calendars);
        Assert.Equal(1, analysis.TotalEventCount);
        Assert.Equal("user@example.com", analysis.Calendars[0].CalendarKey);
        Assert.Equal("Private", analysis.Calendars[0].DisplayName);
    }

    [Fact]
    public async Task ImportAsync_PreservesLegacyFieldsAndGoogleEventId()
    {
        var sourceFolder = CreateLegacyFolder();
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var service = new FavGCalSchedulerImportService(repository);

        var result = await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" }));

        var events = await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset));

        Assert.Equal(1, result.ImportedCount);
        var item = Assert.Single(events);
        Assert.Equal("Legacy todo #todoA56%", item.Title);
        Assert.Equal("Meeting room", item.Location);
        Assert.Equal("Body #Holiday", item.Description);
        Assert.Equal("legacyevent123", item.GoogleEventId);
        Assert.Equal("primary", item.CalendarId);
        Assert.True(item.IsDirty);
        Assert.True(item.IsTodoLike);
        Assert.False(TagService.IsHoliday(item));
        Assert.Equal("5", item.ColorId);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "1")]
    [InlineData(2, "2")]
    public async Task ImportAsync_ReadsLegacyLabelColorFromPackedField(int legacyColorIndex, string? expectedColorId)
    {
        var sourceFolder = CreateLegacyFolder(legacyColorIndex: legacyColorIndex, unrelatedValueAt8: 11);
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var service = new FavGCalSchedulerImportService(repository);

        await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" }));

        var events = await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset));

        Assert.Equal(expectedColorId, Assert.Single(events).ColorId);
    }

    [Fact]
    public async Task ImportAsync_ReadsLegacyTodoRecordWhenMetadataIsPresent()
    {
        var sourceFolder = CreateLegacyFolder(recordKind: 0x06);
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var service = new FavGCalSchedulerImportService(repository);

        var result = await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" }));

        var events = await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset));

        var item = Assert.Single(events);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.UnrestoredTodoCount);
        Assert.True(item.IsTodoLike);
        Assert.Equal("A", item.TodoPriority);
        Assert.Equal(56, item.TodoProgress);
        Assert.Equal("5", item.ColorId);
    }

    [Theory]
    [InlineData(0, 0, "A", false)]
    [InlineData(90, 5, "F", false)]
    [InlineData(100, 2, "C", true)]
    public async Task ImportAsync_RestoresNativeTodoMetadataFromRecordTail(
        int progress,
        short priorityOrdinal,
        string expectedPriority,
        bool expectedDone)
    {
        var sourceFolder = CreateLegacyFolder(
            recordKind: 0x06,
            title: "Legacy native todo",
            todoProgress: progress,
            todoPriorityOrdinal: priorityOrdinal);
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var service = new FavGCalSchedulerImportService(repository);

        var result = await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" }));

        var events = await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset));

        var item = Assert.Single(events);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.UnrestoredTodoCount);
        Assert.True(item.IsTodoLike);
        Assert.Equal(expectedPriority, item.TodoPriority);
        Assert.Equal(progress, item.TodoProgress);
        Assert.Equal(expectedDone, item.IsTodoDone);
        Assert.Contains($"#todo{expectedPriority}{progress}%", item.Description);
        Assert.Equal("legacyevent123", item.GoogleEventId);
        Assert.Equal("5", item.ColorId);
    }

    [Fact]
    public async Task ImportAsync_ReportsLegacyTodoWithoutRecoverableMetadataInsteadOfGuessing()
    {
        var sourceFolder = CreateLegacyFolder(recordKind: 0x06, title: "Legacy local todo");
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var service = new FavGCalSchedulerImportService(repository);

        var analysis = await service.AnalyzeAsync(sourceFolder);
        var result = await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" }));

        Assert.Equal(0, analysis.TotalEventCount);
        Assert.Equal(1, analysis.UnrestoredTodoCount);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.UnrestoredTodoCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("末尾の優先度/進捗値", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(101, 0)]
    [InlineData(50, -1)]
    [InlineData(50, 6)]
    public async Task ImportAsync_RejectsNativeTodoMetadataOutsideLegacyRange(int progress, short priorityOrdinal)
    {
        var sourceFolder = CreateLegacyFolder(
            recordKind: 0x06,
            title: "Invalid native todo",
            todoProgress: progress,
            todoPriorityOrdinal: priorityOrdinal);
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var service = new FavGCalSchedulerImportService(repository);

        var result = await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" }));

        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.UnrestoredTodoCount);
    }

    [Fact]
    public async Task ImportAsync_LinksExistingGoogleEventWithoutDuplicating()
    {
        var sourceFolder = CreateLegacyFolder();
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "existing",
            CalendarId = "primary",
            GoogleEventId = "legacyevent123",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeZoneInfo.Local.BaseUtcOffset)
        });
        var service = new FavGCalSchedulerImportService(repository);

        var result = await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" }));

        var events = await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset));

        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.LinkedExistingGoogleCount);
        Assert.Equal(1, result.CorrectedColorCount);
        Assert.Equal("5", Assert.Single(events).ColorId);
    }

    [Fact]
    public async Task ImportAsync_FillsMissingColorWhenDuplicateIsSkipped()
    {
        var sourceFolder = CreateLegacyFolder(legacyColorIndex: 2);
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Legacy todo #todoA56%",
            Location = "Meeting room",
            CalendarId = "primary",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeZoneInfo.Local.BaseUtcOffset)
        });
        var service = new FavGCalSchedulerImportService(repository);

        var result = await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" }));

        var events = await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset));

        Assert.Equal(1, result.SkippedDuplicateCount);
        Assert.Equal(1, result.CorrectedColorCount);
        Assert.Equal("2", Assert.Single(events).ColorId);
    }

    [Fact]
    public async Task ImportAsync_DoesNotReplaceExistingColorWithoutRepairOption()
    {
        var sourceFolder = CreateLegacyFolder(legacyColorIndex: 2);
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "existing",
            CalendarId = "primary",
            GoogleEventId = "legacyevent123",
            ColorId = "9",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeZoneInfo.Local.BaseUtcOffset)
        });
        var service = new FavGCalSchedulerImportService(repository);

        var result = await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" }));

        var item = await repository.FindEventByGoogleEventIdAsync("primary", "legacyevent123");
        Assert.Equal(0, result.CorrectedColorCount);
        Assert.Equal("9", item!.ColorId);
    }

    [Fact]
    public async Task ImportAsync_RepairOptionClearsIncorrectColorForWhiteLegacyLabel()
    {
        var sourceFolder = CreateLegacyFolder(legacyColorIndex: 0);
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "existing",
            CalendarId = "primary",
            GoogleEventId = "legacyevent123",
            ColorId = "9",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeZoneInfo.Local.BaseUtcOffset)
        });
        var service = new FavGCalSchedulerImportService(repository);

        var result = await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" },
            RepairExistingColors: true));

        var item = await repository.FindEventByGoogleEventIdAsync("primary", "legacyevent123");
        Assert.Equal(1, result.CorrectedColorCount);
        Assert.Null(item!.ColorId);
    }

    [Fact]
    public async Task ImportAsync_PromotesPreviouslyImportedGoogleEventWhenNativeTodoIsReimported()
    {
        var sourceFolder = CreateLegacyFolder(
            recordKind: 0x06,
            title: "Legacy native todo",
            todoProgress: 90,
            todoPriorityOrdinal: 5,
            legacyColorIndex: 2);
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Legacy native todo",
            Description = "Body #Holiday",
            CalendarId = "primary",
            GoogleEventId = "legacyevent123",
            ColorId = "9",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeZoneInfo.Local.BaseUtcOffset)
        });
        var service = new FavGCalSchedulerImportService(repository);

        var result = await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" },
            RepairExistingColors: true));

        var events = await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset));

        var item = Assert.Single(events);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.LinkedExistingGoogleCount);
        Assert.Equal(1, result.CorrectedColorCount);
        Assert.Equal("2", item.ColorId);
        Assert.True(item.IsTodoLike);
        Assert.Equal("F", item.TodoPriority);
        Assert.Equal(90, item.TodoProgress);
        Assert.Contains("#todoF90%", item.Description);
    }

    [Fact]
    public async Task ImportAsync_PreservesMultilineBodyForNewNativeTodo()
    {
        var body = $"first line{Environment.NewLine}{Environment.NewLine}second line";
        var sourceFolder = CreateLegacyFolder(
            recordKind: 0x06,
            title: "Multiline todo",
            description: body,
            todoProgress: 20,
            todoPriorityOrdinal: 0);
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var service = new FavGCalSchedulerImportService(repository);

        await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" }));

        var item = await repository.FindEventByGoogleEventIdAsync("primary", "legacyevent123");
        Assert.Equal(body, TagService.GetTodoBodyForEditing(item!.Description));
    }

    [Fact]
    public async Task ImportAsync_DoesNotReplaceExistingTodoBodyWithoutRepairOption()
    {
        var sourceFolder = CreateLegacyFolder(
            recordKind: 0x06,
            title: "Existing native todo",
            description: $"source line 1{Environment.NewLine}source line 2",
            todoProgress: 20,
            todoPriorityOrdinal: 0);
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Existing native todo",
            Description = "#todoF90%\nlocal text",
            CalendarId = "primary",
            GoogleEventId = "legacyevent123",
            IsTodoLike = true,
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeZoneInfo.Local.BaseUtcOffset)
        });
        var service = new FavGCalSchedulerImportService(repository);

        var result = await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" }));

        var item = await repository.FindEventByGoogleEventIdAsync("primary", "legacyevent123");
        Assert.Equal(0, result.CorrectedTodoDescriptionCount);
        Assert.Equal("local text", TagService.GetTodoBodyForEditing(item!.Description));
        Assert.Equal("F", item.TodoPriority);
        Assert.Equal(90, item.TodoProgress);
    }

    [Fact]
    public async Task ImportAsync_RepairOptionRestoresExistingTodoBodyAndKeepsMetadata()
    {
        var sourceBody = $"source line 1{Environment.NewLine}{Environment.NewLine}source line 2";
        var sourceFolder = CreateLegacyFolder(
            recordKind: 0x06,
            title: "Existing native todo",
            description: sourceBody,
            todoProgress: 20,
            todoPriorityOrdinal: 0);
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Existing native todo",
            Description = "#todoF90% flattened source text",
            CalendarId = "primary",
            GoogleEventId = "legacyevent123",
            IsTodoLike = true,
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeZoneInfo.Local.BaseUtcOffset)
        });
        var service = new FavGCalSchedulerImportService(repository);

        var result = await service.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" },
            RepairExistingTodoDescriptions: true));

        var item = await repository.FindEventByGoogleEventIdAsync("primary", "legacyevent123");
        Assert.Equal(1, result.CorrectedTodoDescriptionCount);
        Assert.Equal(sourceBody, TagService.GetTodoBodyForEditing(item!.Description));
        Assert.Equal("F", item.TodoPriority);
        Assert.Equal(90, item.TodoProgress);
    }

    [Fact]
    public async Task ImportFavGCalSchedulerAsync_CanApplyLegacySettings()
    {
        var sourceFolder = CreateLegacyFolder();
        await File.AppendAllTextAsync(Path.Combine(sourceFolder, "FavGCalScheduler.ini"), """
            [DISP_INFO]
            count=2
            item0=disp-value
            DeletePopup=1
            AppClose=0
            EditScheduleWindowHide=1
            StartWeekdayIndex=1
            WeekdayType=2
            FontSize=2
            BottomInfoFontSize=0
            ToDoRunLimitMonthCount=3
            ToDoCompLimitMonthCount=12
            [APP_INFO]
            count=30
            item0=app-value
            CreateScheduleNoHistory=0
            ScheduleDeaultAllDay=0
            ScheduleDeaultAlarmIndex=3
            [SYNC_INFO]
            AddEditDelSync=1
            SyncIntervalMin=120
            """);
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();

        await viewModel.ImportFavGCalSchedulerAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" },
            VerifyGoogleEventsBeforeImport: false));

        Assert.True(viewModel.ConfirmBeforeDelete);
        Assert.False(viewModel.CloseButtonExitsApplication);
        Assert.False(viewModel.DefaultNewEventIsAllDay);
        var settings = viewModel.CreateSettingsSnapshot();
        Assert.True(settings.HideMainWindowWhileEditingSchedule);
        Assert.True(settings.WeekStartsOnMonday);
        Assert.Equal(WeekdayDisplayType.JapaneseShort, settings.WeekdayDisplayType);
        Assert.Equal(3, settings.CalendarLabelFontSizeIndex);
        Assert.Equal(1, settings.SideListFontSizeIndex);
        Assert.Equal(3, settings.IncompleteTodoDisplayPeriodMonths);
        Assert.Equal(12, settings.CompletedTodoDisplayPeriodMonths);
        Assert.True(settings.ReuseLastScheduleInput);
        Assert.Equal(10, settings.DefaultScheduleReminderMinutes);
        Assert.True(settings.SyncAfterLocalChange);
        Assert.Equal(120, settings.AutomaticSyncIntervalMinutes);
    }

    [Fact]
    public async Task ImportFavGCalSchedulerAsync_PreservesSettingsNotPresentInLegacyIni()
    {
        var sourceFolder = CreateLegacyFolder();
        await File.AppendAllTextAsync(Path.Combine(sourceFolder, "FavGCalScheduler.ini"), """
            [DISP_INFO]
            AppClose=1
            [OTHER]
            DeletePopup=1
            ScheduleDeaultAllDay=1
            """);
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        await viewModel.SaveApplicationSettingsAsync(0, false, false, false, true);

        await viewModel.ImportFavGCalSchedulerAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" },
            VerifyGoogleEventsBeforeImport: false));

        Assert.False(viewModel.ConfirmBeforeDelete);
        Assert.True(viewModel.CloseButtonExitsApplication);
        Assert.False(viewModel.DefaultNewEventIsAllDay);
    }

    [Fact]
    public async Task CompareService_LoadsZipAndMatchesImportedEvent()
    {
        var zipPath = CreateIcalZip();
        var sourceFolder = CreateLegacyFolder();
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var importService = new FavGCalSchedulerImportService(repository);
        var compareService = new GoogleCalendarExportCompareService();

        await importService.ImportAsync(new FavGCalImportOptions(
            sourceFolder,
            new Dictionary<string, string> { ["user@example.com"] = "primary" }));
        var imported = await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
            new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset));
        var exported = await compareService.LoadFromZipAsync(zipPath);

        var summary = compareService.Compare(imported, exported.Events);

        Assert.Equal("Private", exported.CalendarName);
        Assert.Single(exported.Events);
        Assert.Equal(1, summary.MatchedCount);
        Assert.Equal(0, summary.LocalOnlyCount);
        Assert.Equal(0, summary.ExportOnlyCount);
    }

    private static string CreateLegacyFolder(
        byte recordKind = 0x01,
        string title = "Legacy todo #todoA56%",
        string description = "Body #Holiday",
        int? todoProgress = null,
        short? todoPriorityOrdinal = null,
        int legacyColorIndex = 5,
        int unrelatedValueAt8 = 0)
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "schedule.ini"), """
            [VERSION]
            version=2.0.1
            [CALENDAR_ITEM]
            count=1
            item0=.\FavSchedule1.favcal
            disp0=1
            """);
        File.WriteAllBytes(
            Path.Combine(folder, "FavSchedule1.favcal"),
            CreateFavCalBytes(recordKind, title, description, todoProgress, todoPriorityOrdinal, legacyColorIndex, unrelatedValueAt8));
        return folder;
    }

    private static string CreateIcalZip()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        var ics = """
            BEGIN:VCALENDAR
            PRODID:-//Google Inc//Google Calendar 70.9054//EN
            VERSION:2.0
            CALSCALE:GREGORIAN
            METHOD:PUBLISH
            X-WR-CALNAME:Private
            X-WR-TIMEZONE:Asia/Tokyo
            BEGIN:VEVENT
            DTSTART:20260516T000000Z
            DTEND:20260516T010000Z
            UID:legacyevent123
            SUMMARY:Legacy todo #todoA56%
            LOCATION:Meeting room
            DESCRIPTION:Body #Holiday
            END:VEVENT
            END:VCALENDAR
            """.Replace("\r\n", "\n").Replace("\n", "\r\n");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("user@example.com.ics");
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(ics);
        return zipPath;
    }

    private static byte[] CreateFavCalBytes(
        byte recordKind,
        string title,
        string description = "Body #Holiday",
        int? todoProgress = null,
        short? todoPriorityOrdinal = null,
        int legacyColorIndex = 5,
        int unrelatedValueAt8 = 0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);
        writer.Write(Encoding.Unicode.GetBytes("FavSchedule"));
        writer.Write(new byte[32]);
        WriteHeaderString(writer, "Private");
        WriteHeaderString(writer, "https://www.google.com/calendar/feeds/user%40example.com/private/full");
        writer.Write(new byte[32]);

        writer.Write(new byte[] { 0x08, 0x00, recordKind, 0x00 });
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write(unrelatedValueAt8);
        writer.Write(new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeZoneInfo.Local.BaseUtcOffset).ToUnixTimeSeconds());
        writer.Write(new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeZoneInfo.Local.BaseUtcOffset).ToUnixTimeSeconds());
        writer.Write(60);
        writer.Write((ushort)(legacyColorIndex << 8));
        WriteFavString(writer, title);
        WriteFavString(writer, "Meeting room");
        WriteFavString(writer, description);
        writer.Write(0);
        WriteGoogleId(writer, "legacyevent123");
        if (todoProgress.HasValue && todoPriorityOrdinal.HasValue)
        {
            writer.Write(todoProgress.Value);
            writer.Write(todoPriorityOrdinal.Value);
        }

        return stream.ToArray();
    }

    private static void WriteHeaderString(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.Unicode.GetBytes(value));
        writer.Write((ushort)0);
    }

    private static void WriteFavString(BinaryWriter writer, string value)
    {
        writer.Write(value.Length);
        writer.Write(Encoding.Unicode.GetBytes(value));
        writer.Write((ushort)0);
    }

    private static void WriteGoogleId(BinaryWriter writer, string value)
    {
        writer.Write(value.Length);
        writer.Write(Encoding.Unicode.GetBytes(value));
    }
}
