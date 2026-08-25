using System.IO.Compression;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class GoogleCalendarExportTimeZoneTests
{
    [Theory]
    [InlineData("20260115T090000", -5, 14)]
    [InlineData("20260715T090000", -4, 13)]
    public async Task LoadFromZipAsync_UsesIcsTzidInsteadOfMachineLocalZone(string localStart, int expectedOffsetHours, int expectedUtcHour)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ics-tz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var zipPath = Path.Combine(directory, "takeout.zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("Calendar/test.ics");
                await using var stream = entry.Open();
                await using var writer = new StreamWriter(stream);
                var end = localStart.Replace("T090000", "T100000", StringComparison.Ordinal);
                await writer.WriteAsync($"""
                    BEGIN:VCALENDAR
                    VERSION:2.0
                    X-WR-CALNAME:TZ Test
                    BEGIN:VEVENT
                    UID:tz-test
                    DTSTART;TZID=America/New_York:{localStart}
                    DTEND;TZID=America/New_York:{end}
                    SUMMARY:New York meeting
                    END:VEVENT
                    END:VCALENDAR
                    """);
            }

            var data = await new GoogleCalendarExportCompareService().LoadFromZipAsync(zipPath);
            var item = Assert.Single(data.Events);

            Assert.Equal(TimeSpan.FromHours(expectedOffsetHours), item.Start.Offset);
            Assert.Equal(expectedUtcHour, item.Start.UtcDateTime.Hour);
            Assert.Equal(expectedUtcHour + 1, item.End.UtcDateTime.Hour);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadFromZipAsync_LoadsEventsFromEveryIcsEntry()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ics-multi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var zipPath = Path.Combine(directory, "takeout.zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                await WriteCalendarAsync(archive, "Calendar/primary.ics", "Primary", "primary-event", "Primary event");
                await WriteCalendarAsync(archive, "Calendar/team.ics", "Team", "team-event", "Team event");
            }

            var data = await new GoogleCalendarExportCompareService().LoadFromZipAsync(zipPath);

            Assert.Equal(2, data.Events.Count);
            Assert.Contains(data.Events, item => item.Uid == "primary-event" && item.Summary == "Primary event");
            Assert.Contains(data.Events, item => item.Uid == "team-event" && item.Summary == "Team event");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task WriteCalendarAsync(ZipArchive archive, string path, string name, string uid, string summary)
    {
        var entry = archive.CreateEntry(path);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync($"""
            BEGIN:VCALENDAR
            VERSION:2.0
            X-WR-CALNAME:{name}
            BEGIN:VEVENT
            UID:{uid}
            DTSTART:20260825T090000Z
            DTEND:20260825T100000Z
            SUMMARY:{summary}
            END:VEVENT
            END:VCALENDAR
            """);
    }
}
