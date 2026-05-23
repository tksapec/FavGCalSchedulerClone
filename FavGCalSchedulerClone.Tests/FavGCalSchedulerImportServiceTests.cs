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
        Assert.True(TagService.IsHoliday(item));
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
        Assert.Contains(result.Warnings, warning => warning.Contains("ToDo 1", StringComparison.Ordinal));
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
        Assert.Single(events);
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
            [APP_INFO]
            count=30
            item0=app-value
            ScheduleDeaultAllDay=0
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

    private static string CreateLegacyFolder(byte recordKind = 0x01, string title = "Legacy todo #todoA56%")
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
        File.WriteAllBytes(Path.Combine(folder, "FavSchedule1.favcal"), CreateFavCalBytes(recordKind, title));
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

    private static byte[] CreateFavCalBytes(byte recordKind, string title)
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
        writer.Write(5);
        writer.Write(new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeZoneInfo.Local.BaseUtcOffset).ToUnixTimeSeconds());
        writer.Write(new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeZoneInfo.Local.BaseUtcOffset).ToUnixTimeSeconds());
        writer.Write(60);
        writer.Write((ushort)0);
        WriteFavString(writer, title);
        WriteFavString(writer, "Meeting room");
        WriteFavString(writer, "Body #Holiday");
        writer.Write(0);
        WriteGoogleId(writer, "legacyevent123");
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
